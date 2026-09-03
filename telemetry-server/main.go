package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"sync"
	"syscall"
	"time"
)

const (
	defaultListenAddress = ":4318"
	defaultDataDirectory = "/var/lib/touchpilot-telemetry"
	maximumBodyBytes     = 32 * 1024
	requestsPerMinute    = 120
	maximumRateVisitors  = 16384
)

var version = "dev"

type eventPayload struct {
	SchemaVersion int               `json:"schemaVersion"`
	EventID       string            `json:"eventId"`
	ClientID      string            `json:"clientId"`
	SessionID     string            `json:"sessionId"`
	Name          string            `json:"name"`
	OccurredAtUTC time.Time         `json:"occurredAtUtc"`
	AppVersion    string            `json:"appVersion"`
	Distribution  string            `json:"distribution"`
	Runtime       string            `json:"runtime"`
	Properties    map[string]string `json:"properties"`
}

type storedEvent struct {
	eventPayload
	ReceivedAtUTC time.Time `json:"receivedAtUtc"`
}

type storeRequest struct {
	event  storedEvent
	result chan error
}

type eventStore struct {
	directory string
	requests  chan storeRequest
	done      chan struct{}
}

func newEventStore(directory string) (*eventStore, error) {
	if strings.TrimSpace(directory) == "" {
		return nil, errors.New("data directory is required")
	}
	if err := os.MkdirAll(directory, 0o750); err != nil {
		return nil, fmt.Errorf("create data directory: %w", err)
	}
	store := &eventStore{
		directory: directory,
		requests:  make(chan storeRequest, 256),
		done:      make(chan struct{}),
	}
	go store.run()
	return store, nil
}

func (s *eventStore) append(ctx context.Context, event storedEvent) error {
	request := storeRequest{event: event, result: make(chan error, 1)}
	select {
	case s.requests <- request:
	case <-ctx.Done():
		return ctx.Err()
	}
	select {
	case err := <-request.result:
		return err
	case <-ctx.Done():
		return ctx.Err()
	}
}

func (s *eventStore) close() {
	close(s.requests)
	<-s.done
}

func (s *eventStore) run() {
	defer close(s.done)
	var file *os.File
	var currentDay string
	defer func() {
		if file != nil {
			_ = file.Sync()
			_ = file.Close()
		}
	}()

	for request := range s.requests {
		day := request.event.ReceivedAtUTC.Format("2006-01-02")
		if day != currentDay {
			if file != nil {
				_ = file.Sync()
				_ = file.Close()
				file = nil
			}
			path := filepath.Join(s.directory, "events-"+day+".jsonl")
			opened, err := os.OpenFile(path, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0o640)
			if err != nil {
				request.result <- fmt.Errorf("open event file: %w", err)
				continue
			}
			file = opened
			currentDay = day
		}

		encoded, err := json.Marshal(request.event)
		if err == nil {
			encoded = append(encoded, '\n')
			_, err = file.Write(encoded)
		}
		if err != nil {
			err = fmt.Errorf("append event: %w", err)
		}
		request.result <- err
	}
}

type requestWindow struct {
	started time.Time
	count   int
}

type rateLimiter struct {
	mu       sync.Mutex
	visitors map[string]requestWindow
	limit    int
	window   time.Duration
}

func newRateLimiter(limit int, window time.Duration) *rateLimiter {
	return &rateLimiter{visitors: make(map[string]requestWindow), limit: limit, window: window}
}

func (l *rateLimiter) allow(address string, now time.Time) bool {
	host, _, err := net.SplitHostPort(address)
	if err != nil {
		host = address
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	entry := l.visitors[host]
	if entry.started.IsZero() || now.Sub(entry.started) >= l.window {
		l.cleanup(now)
		if _, exists := l.visitors[host]; !exists && len(l.visitors) >= maximumRateVisitors {
			return false
		}
		l.visitors[host] = requestWindow{started: now, count: 1}
		return true
	}
	if entry.count >= l.limit {
		return false
	}
	entry.count++
	l.visitors[host] = entry
	return true
}

func (l *rateLimiter) cleanup(now time.Time) {
	if len(l.visitors) < 4096 {
		return
	}
	for address, entry := range l.visitors {
		if now.Sub(entry.started) >= 2*l.window {
			delete(l.visitors, address)
		}
	}
}

func main() {
	if err := run(); err != nil {
		log.Fatal(err)
	}
}

func run() error {
	listenAddress := environmentOrDefault("TOUCHPILOT_TELEMETRY_LISTEN", defaultListenAddress)
	dataDirectory := environmentOrDefault("TOUCHPILOT_TELEMETRY_DATA_DIR", defaultDataDirectory)
	store, err := newEventStore(dataDirectory)
	if err != nil {
		return err
	}

	server := &http.Server{
		Addr:              listenAddress,
		Handler:           newHandler(store),
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       10 * time.Second,
		WriteTimeout:      10 * time.Second,
		IdleTimeout:       60 * time.Second,
		MaxHeaderBytes:    16 * 1024,
	}

	serverErrors := make(chan error, 1)
	go func() {
		log.Printf("TouchPilot telemetry %s listening on %s", version, listenAddress)
		serverErrors <- server.ListenAndServe()
	}()

	signals := make(chan os.Signal, 1)
	signal.Notify(signals, syscall.SIGINT, syscall.SIGTERM)
	defer signal.Stop(signals)
	var runErr error
	select {
	case received := <-signals:
		log.Printf("received %s, shutting down", received)
	case serveErr := <-serverErrors:
		if !errors.Is(serveErr, http.ErrServerClosed) {
			runErr = fmt.Errorf("HTTP server failed: %w", serveErr)
		}
	}

	shutdownContext, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	if err := server.Shutdown(shutdownContext); err != nil {
		if runErr == nil {
			runErr = fmt.Errorf("HTTP shutdown failed: %w", err)
		}
	}
	store.close()
	return runErr
}

func newHandler(store *eventStore) http.Handler {
	limiter := newRateLimiter(requestsPerMinute, time.Minute)
	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", func(writer http.ResponseWriter, request *http.Request) {
		writeJSON(writer, http.StatusOK, map[string]string{"status": "ok", "version": version})
	})
	mux.HandleFunc("POST /v1/events", func(writer http.ResponseWriter, request *http.Request) {
		now := time.Now().UTC()
		if !limiter.allow(request.RemoteAddr, now) {
			writer.Header().Set("Retry-After", "60")
			writeJSON(writer, http.StatusTooManyRequests, map[string]string{"error": "rate limit exceeded"})
			return
		}
		if mediaType := strings.ToLower(strings.TrimSpace(strings.Split(
			request.Header.Get("Content-Type"), ";")[0])); mediaType != "application/json" {
			writeJSON(writer, http.StatusUnsupportedMediaType,
				map[string]string{"error": "content type must be application/json"})
			return
		}

		request.Body = http.MaxBytesReader(writer, request.Body, maximumBodyBytes)
		defer request.Body.Close()
		decoder := json.NewDecoder(request.Body)
		var payload eventPayload
		if err := decoder.Decode(&payload); err != nil {
			writeJSON(writer, http.StatusBadRequest, map[string]string{"error": "invalid event payload"})
			return
		}
		if err := ensureJSONEnd(decoder); err != nil {
			writeJSON(writer, http.StatusBadRequest, map[string]string{"error": "invalid event payload"})
			return
		}
		if err := validateEvent(payload, now); err != nil {
			writeJSON(writer, http.StatusBadRequest, map[string]string{"error": err.Error()})
			return
		}

		writeContext, cancel := context.WithTimeout(request.Context(), 3*time.Second)
		defer cancel()
		if err := store.append(writeContext, storedEvent{
			eventPayload:  payload,
			ReceivedAtUTC: now,
		}); err != nil {
			writeJSON(writer, http.StatusServiceUnavailable,
				map[string]string{"error": "event storage unavailable"})
			return
		}
		writeJSON(writer, http.StatusAccepted, map[string]string{"status": "accepted"})
	})
	return mux
}

func validateEvent(event eventPayload, now time.Time) error {
	if event.SchemaVersion != 1 {
		return errors.New("unsupported schema version")
	}
	if !isHexIdentifier(event.EventID, 32) || !isHexIdentifier(event.ClientID, 32) ||
		!isHexIdentifier(event.SessionID, 32) {
		return errors.New("invalid event identity")
	}
	if !isEventName(event.Name) {
		return errors.New("invalid event name")
	}
	if event.OccurredAtUTC.IsZero() || event.OccurredAtUTC.Before(now.AddDate(-1, 0, 0)) ||
		event.OccurredAtUTC.After(now.Add(24*time.Hour)) {
		return errors.New("invalid event timestamp")
	}
	if !isShortValue(event.AppVersion, 64) || !isShortValue(event.Distribution, 32) ||
		!isShortValue(event.Runtime, 32) {
		return errors.New("invalid application identity")
	}
	if len(event.Properties) > 16 {
		return errors.New("too many event properties")
	}
	for key, value := range event.Properties {
		if !isEventName(key) || len(value) > 256 {
			return errors.New("invalid event property")
		}
	}
	return nil
}

func isEventName(value string) bool {
	if len(value) == 0 || len(value) > 64 || value[0] < 'a' || value[0] > 'z' {
		return false
	}
	for _, character := range value {
		if (character < 'a' || character > 'z') && (character < '0' || character > '9') &&
			character != '_' {
			return false
		}
	}
	return true
}

func isHexIdentifier(value string, expectedLength int) bool {
	if len(value) != expectedLength {
		return false
	}
	for _, character := range value {
		if (character < '0' || character > '9') && (character < 'a' || character > 'f') {
			return false
		}
	}
	return true
}

func isShortValue(value string, maximumLength int) bool {
	return strings.TrimSpace(value) != "" && len(value) <= maximumLength
}

func ensureJSONEnd(decoder *json.Decoder) error {
	var extra any
	err := decoder.Decode(&extra)
	if errors.Is(err, io.EOF) {
		return nil
	}
	if err == nil {
		return errors.New("multiple JSON values")
	}
	return err
}

func writeJSON(writer http.ResponseWriter, status int, value any) {
	writer.Header().Set("Content-Type", "application/json; charset=utf-8")
	writer.Header().Set("X-Content-Type-Options", "nosniff")
	writer.WriteHeader(status)
	_ = json.NewEncoder(writer).Encode(value)
}

func environmentOrDefault(name, fallback string) string {
	if value := strings.TrimSpace(os.Getenv(name)); value != "" {
		return value
	}
	return fallback
}

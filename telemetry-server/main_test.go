package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestEventEndpointStoresAcceptedEventWithoutRemoteAddress(t *testing.T) {
	directory := t.TempDir()
	store, err := newEventStore(directory)
	if err != nil {
		t.Fatal(err)
	}
	now := time.Now().UTC()
	payload := validEvent(now)
	body, err := json.Marshal(payload)
	if err != nil {
		t.Fatal(err)
	}
	request := httptest.NewRequest(http.MethodPost, "/v1/events", strings.NewReader(string(body)))
	request.Header.Set("Content-Type", "application/json")
	request.RemoteAddr = "203.0.113.42:12345"
	recorder := httptest.NewRecorder()

	newHandler(store).ServeHTTP(recorder, request)
	store.close()

	if recorder.Code != http.StatusAccepted {
		t.Fatalf("status = %d, body = %s", recorder.Code, recorder.Body.String())
	}
	path := filepath.Join(directory, "events-"+now.Format("2006-01-02")+".jsonl")
	stored, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(stored), `"name":"application_started"`) {
		t.Fatalf("event not stored: %s", stored)
	}
	if strings.Contains(string(stored), "203.0.113.42") {
		t.Fatal("remote address must not be persisted")
	}
}

func TestEventEndpointRejectsInvalidIdentity(t *testing.T) {
	store, err := newEventStore(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	defer store.close()
	payload := validEvent(time.Now().UTC())
	payload.ClientID = "invalid"
	body, _ := json.Marshal(payload)
	request := httptest.NewRequest(http.MethodPost, "/v1/events", strings.NewReader(string(body)))
	request.Header.Set("Content-Type", "application/json")
	recorder := httptest.NewRecorder()

	newHandler(store).ServeHTTP(recorder, request)

	if recorder.Code != http.StatusBadRequest {
		t.Fatalf("status = %d", recorder.Code)
	}
}

func TestHealthEndpoint(t *testing.T) {
	store, err := newEventStore(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	defer store.close()
	request := httptest.NewRequest(http.MethodGet, "/healthz", nil)
	recorder := httptest.NewRecorder()

	newHandler(store).ServeHTTP(recorder, request)

	if recorder.Code != http.StatusOK || !strings.Contains(recorder.Body.String(), `"status":"ok"`) {
		t.Fatalf("status = %d, body = %s", recorder.Code, recorder.Body.String())
	}
}

func validEvent(now time.Time) eventPayload {
	return eventPayload{
		SchemaVersion: 1,
		EventID:       strings.Repeat("a", 32),
		ClientID:      strings.Repeat("b", 32),
		SessionID:     strings.Repeat("c", 32),
		Name:          "application_started",
		OccurredAtUTC: now,
		AppVersion:    "8.2.0",
		Distribution:  "installer",
		Runtime:       "win-x64",
		Properties:    map[string]string{},
	}
}

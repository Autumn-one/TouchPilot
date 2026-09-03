using GestureSign.Common.Updates;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GestureSign.Common.Telemetry
{
    public interface ITelemetrySink
    {
        void Track(string name, IReadOnlyDictionary<string, string> properties = null);
    }

    public sealed class NullTelemetrySink : ITelemetrySink
    {
        public static NullTelemetrySink Instance { get; } = new NullTelemetrySink();

        private NullTelemetrySink()
        {
        }

        public void Track(string name, IReadOnlyDictionary<string, string> properties = null)
        {
        }
    }

    public sealed class TelemetryReporter : ITelemetrySink, IDisposable
    {
        private static readonly TimeSpan EndpointTimeout = TimeSpan.FromSeconds(5);
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly GitHubRepository _repository;
        private readonly string _storageDirectory;
        private readonly string _appVersion;
        private readonly string _distribution;
        private readonly string _runtime;
        private readonly Action<Exception> _logException;
        private readonly HttpClient _httpClient;
        private readonly Channel<TelemetryEvent> _events;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly string _sessionId = Guid.NewGuid().ToString("N");
        private Task _worker;
        private int _started;
        private int _disposed;

        public TelemetryReporter(GitHubRepository repository, string storageDirectory,
            string appVersion, string distribution, string runtime, Action<Exception> logException)
            : this(repository, storageDirectory, appVersion, distribution, runtime, logException,
                CreateHttpClient())
        {
        }

        internal TelemetryReporter(GitHubRepository repository, string storageDirectory,
            string appVersion, string distribution, string runtime, Action<Exception> logException,
            HttpClient httpClient)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _storageDirectory = string.IsNullOrWhiteSpace(storageDirectory)
                ? throw new ArgumentException("A telemetry storage directory is required.",
                    nameof(storageDirectory))
                : Path.GetFullPath(storageDirectory);
            _appVersion = RequireValue(appVersion, nameof(appVersion));
            _distribution = RequireValue(distribution, nameof(distribution));
            _runtime = RequireValue(runtime, nameof(runtime));
            _logException = logException ?? throw new ArgumentNullException(nameof(logException));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _events = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(32)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
                return;
            _worker = Task.Run(() => RunAsync(_shutdown.Token));
        }

        public void Track(string name, IReadOnlyDictionary<string, string> properties = null)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            ValidateEventName(name);
            _events.Writer.TryWrite(new TelemetryEvent
            {
                SchemaVersion = 1,
                EventId = Guid.NewGuid().ToString("N"),
                Name = name,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                SessionId = _sessionId,
                AppVersion = _appVersion,
                Distribution = _distribution,
                Runtime = _runtime,
                Properties = CopyProperties(properties)
            });
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _events.Writer.TryComplete();
            if (_worker != null && !_worker.Wait(TimeSpan.FromMilliseconds(500)))
            {
                _shutdown.Cancel();
                try
                {
                    _worker.Wait(TimeSpan.FromMilliseconds(500));
                }
                catch (AggregateException exception) when (exception.InnerExceptions.All(inner =>
                           inner is OperationCanceledException))
                {
                }
            }
            _shutdown.Dispose();
            _httpClient.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                Directory.CreateDirectory(_storageDirectory);
                string clientId = LoadOrCreateClientId();
                string cachePath = Path.Combine(_storageDirectory, "endpoint-config.json");
                using ECDsa trustedKey = TrustedUpdateSigningKey.Load();
                using var configurationClient = new TelemetryConfigurationClient(
                    _repository, trustedKey);
                TelemetryConfigurationResult result = await configurationClient.GetAsync(cachePath,
                    cancellationToken).ConfigureAwait(false);

                await foreach (TelemetryEvent telemetryEvent in _events.Reader.ReadAllAsync(
                                   cancellationToken).ConfigureAwait(false))
                {
                    telemetryEvent.ClientId = clientId;
                    try
                    {
                        await SendAsync(result.Configuration, telemetryEvent, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _logException(exception);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _logException(exception);
            }
        }

        private async Task SendAsync(TelemetryConfiguration configuration,
            TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
        {
            var errors = new List<Exception>();
            foreach (TelemetryEndpoint endpoint in configuration.Endpoints)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(EndpointTimeout);
                    using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                        endpoint.BuildEventUri(), telemetryEvent, JsonOptions, timeout.Token)
                        .ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    return;
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    errors.Add(new TimeoutException("The telemetry endpoint timed out.", exception));
                }
                catch (Exception exception) when (exception is HttpRequestException ||
                                                  exception is IOException ||
                                                  exception is InvalidDataException)
                {
                    errors.Add(exception);
                }
            }
            throw new AggregateException("Every telemetry endpoint failed.", errors);
        }

        private string LoadOrCreateClientId()
        {
            string path = Path.Combine(_storageDirectory, "client-id");
            if (File.Exists(path))
            {
                string stored = File.ReadAllText(path).Trim();
                if (Guid.TryParseExact(stored, "N", out Guid parsed))
                    return parsed.ToString("N");
            }

            string clientId = Guid.NewGuid().ToString("N");
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, clientId, new UTF8Encoding(false));
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            return clientId;
        }

        private static Dictionary<string, string> CopyProperties(
            IReadOnlyDictionary<string, string> properties)
        {
            if (properties == null || properties.Count == 0)
                return new Dictionary<string, string>();
            if (properties.Count > 16)
                throw new ArgumentException("Telemetry events support at most 16 properties.",
                    nameof(properties));

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> property in properties)
            {
                if (!IsEventName(property.Key) ||
                    property.Value == null || property.Value.Length > 256)
                    throw new ArgumentException("A telemetry event property is invalid.",
                        nameof(properties));
                result.Add(property.Key, property.Value);
            }
            return result;
        }

        private static void ValidateEventName(string name)
        {
            if (!IsEventName(name))
                throw new ArgumentException("A telemetry event name must use lowercase letters, digits, and underscores.",
                    nameof(name));
        }

        private static bool IsEventName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length <= 64 &&
                   name[0] >= 'a' && name[0] <= 'z' && name.All(character =>
                       character >= 'a' && character <= 'z' ||
                       character >= '0' && character <= '9' || character == '_');
        }

        private static string RequireValue(string value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A telemetry identity value is required.", name)
                : value.Trim();
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TouchPilot-Telemetry");
            return client;
        }

        private sealed class TelemetryEvent
        {
            public int SchemaVersion { get; set; }
            public string EventId { get; set; }
            public string ClientId { get; set; }
            public string SessionId { get; set; }
            public string Name { get; set; }
            public DateTimeOffset OccurredAtUtc { get; set; }
            public string AppVersion { get; set; }
            public string Distribution { get; set; }
            public string Runtime { get; set; }
            public Dictionary<string, string> Properties { get; set; }
        }
    }
}

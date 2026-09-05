using GestureSign.Common.Updates;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.Common.Telemetry
{
    public sealed class TelemetryConfigurationClient : IDisposable
    {
        public const string RepositoryPath = "distribution/telemetry-endpoint.json";
        private const int MaximumConfigurationBytes = 64 * 1024;
        private static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(5);

        private readonly GitHubRepository _repository;
        private readonly ECDsa _trustedKey;
        private readonly HttpClient _httpClient;
        private readonly IReadOnlyList<UpdateSource> _sources;
        private readonly TimeSpan _sourceTimeout;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly bool _ownsHttpClient;

        public TelemetryConfigurationClient(GitHubRepository repository, ECDsa trustedKey)
            : this(repository, trustedKey, CreateHttpClient(), UpdateSource.CreateDefaults(),
                DefaultSourceTimeout, () => DateTimeOffset.UtcNow, true)
        {
        }

        internal TelemetryConfigurationClient(GitHubRepository repository, ECDsa trustedKey,
            HttpClient httpClient, IReadOnlyList<UpdateSource> sources, TimeSpan sourceTimeout,
            Func<DateTimeOffset> utcNow, bool ownsHttpClient = false)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _trustedKey = trustedKey ?? throw new ArgumentNullException(nameof(trustedKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _sources = sources == null || sources.Count == 0
                ? throw new ArgumentException("At least one telemetry configuration source is required.",
                    nameof(sources))
                : sources;
            _sourceTimeout = sourceTimeout > TimeSpan.Zero
                ? sourceTimeout
                : throw new ArgumentOutOfRangeException(nameof(sourceTimeout));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _ownsHttpClient = ownsHttpClient;
        }

        public Task<TelemetryConfigurationResult> GetAsync(string cachePath,
            CancellationToken cancellationToken)
        {
            return GetAsync(cachePath, cancellationToken, 0);
        }

        public async Task<TelemetryConfigurationResult> GetAsync(string cachePath,
            CancellationToken cancellationToken, long failedRevision)
        {
            string targetUrl = BuildConfigurationUrl();
            var errors = new List<string>();
            TelemetryConfiguration cachedConfiguration = TryReadCache(cachePath, errors);
            foreach (UpdateSource source in _sources)
            {
                Candidate candidate = await DownloadAsync(source, targetUrl, cancellationToken)
                    .ConfigureAwait(false);
                if (candidate.Error != null)
                {
                    errors.Add(source.Name + ": " + candidate.Error.Message);
                    continue;
                }

                try
                {
                    TelemetryConfiguration configuration =
                        TelemetryConfigurationSignature.Verify(candidate.Json, _trustedKey);
                    if (!string.Equals(configuration.Repository, _repository.Slug,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "The telemetry configuration repository does not match the application.");
                    if (cachedConfiguration != null &&
                        configuration.Revision < cachedConfiguration.Revision)
                        throw new InvalidDataException(
                            "The telemetry configuration revision is older than the verified cache.");
                    if (configuration.Revision <= failedRevision)
                        throw new InvalidDataException(
                            "This telemetry revision already failed delivery; checking the next mirror.");
                    TryWriteCache(cachePath, candidate.Json);
                    return new TelemetryConfigurationResult(configuration, source.Name, false,
                        candidate.ElapsedMilliseconds);
                }
                catch (Exception exception) when (exception is InvalidDataException ||
                                                  exception is FormatException ||
                                                  exception is CryptographicException)
                {
                    errors.Add(source.Name + ": " + exception.Message);
                }
            }

            if (cachedConfiguration != null)
                return new TelemetryConfigurationResult(cachedConfiguration, "cache", true, 0);

            throw new TelemetryConfigurationUnavailableException(errors);
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }

        private string BuildConfigurationUrl()
        {
            long cacheKey = _utcNow().ToUnixTimeSeconds() / (long)TimeSpan.FromMinutes(10).TotalSeconds;
            return $"https://raw.githubusercontent.com/{_repository.Slug}/main/{RepositoryPath}" +
                   $"?touchpilot_config={cacheKey}";
        }

        private async Task<Candidate> DownloadAsync(UpdateSource source, string targetUrl,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_sourceTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, source.Prefix + targetUrl);
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                {
                    NoCache = true
                };
                using HttpResponseMessage response = await _httpClient.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumConfigurationBytes)
                    throw new InvalidDataException("The telemetry configuration exceeds the size limit.");
                string json = await ReadLimitedStringAsync(response, timeout.Token).ConfigureAwait(false);
                return Candidate.Success(json, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                return Candidate.Failure(new TimeoutException(
                    "The telemetry configuration source timed out.", exception),
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception exception) when (exception is HttpRequestException ||
                                              exception is IOException ||
                                              exception is InvalidDataException)
            {
                return Candidate.Failure(exception, stopwatch.ElapsedMilliseconds);
            }
        }

        private static async Task<string> ReadLimitedStringAsync(HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var memory = new MemoryStream();
            byte[] buffer = new byte[8192];
            int total = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;
                total += read;
                if (total > MaximumConfigurationBytes)
                    throw new InvalidDataException("The telemetry configuration exceeds the size limit.");
                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            return Encoding.UTF8.GetString(memory.ToArray());
        }

        private static void TryWriteCache(string path, string json)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                    File.Move(temporaryPath, fullPath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
            }
        }

        private TelemetryConfiguration TryReadCache(string path, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            try
            {
                TelemetryConfiguration configuration = TelemetryConfigurationSignature.Verify(
                    File.ReadAllText(path), _trustedKey);
                if (!string.Equals(configuration.Repository, _repository.Slug,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "The cached telemetry repository does not match the application.");
                return configuration;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is InvalidDataException ||
                                              exception is CryptographicException ||
                                              exception is FormatException)
            {
                errors.Add("cache: " + exception.Message);
                return null;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TouchPilot-Telemetry-Config");
            return client;
        }

        private sealed class Candidate
        {
            private Candidate(string json, Exception error, long elapsedMilliseconds)
            {
                Json = json;
                Error = error;
                ElapsedMilliseconds = elapsedMilliseconds;
            }

            public string Json { get; }
            public Exception Error { get; }
            public long ElapsedMilliseconds { get; }

            public static Candidate Success(string json, long elapsedMilliseconds)
            {
                return new Candidate(json, null, elapsedMilliseconds);
            }

            public static Candidate Failure(Exception error, long elapsedMilliseconds)
            {
                return new Candidate(null, error, elapsedMilliseconds);
            }
        }
    }

    public sealed class TelemetryConfigurationResult
    {
        internal TelemetryConfigurationResult(TelemetryConfiguration configuration, string sourceName,
            bool fromCache, long elapsedMilliseconds)
        {
            Configuration = configuration;
            SourceName = sourceName;
            FromCache = fromCache;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public TelemetryConfiguration Configuration { get; }
        public string SourceName { get; }
        public bool FromCache { get; }
        public long ElapsedMilliseconds { get; }
    }

    public sealed class TelemetryConfigurationUnavailableException : Exception
    {
        public TelemetryConfigurationUnavailableException(IReadOnlyList<string> errors)
            : base("No trusted telemetry configuration source was available. " +
                   string.Join(" | ", errors ?? Array.Empty<string>()))
        {
            Errors = errors ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> Errors { get; }
    }
}

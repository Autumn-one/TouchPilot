using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace GestureSign.Common.Updates
{
    public sealed class UpdateMetadataClient : IDisposable
    {
        private const int MaximumMetadataBytes = 64 * 1024;
        private static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(6);

        private readonly GitHubRepository _repository;
        private readonly ECDsa _trustedKey;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly IReadOnlyList<UpdateSource> _sources;
        private readonly TimeSpan _sourceTimeout;
        private readonly Func<DateTimeOffset> _utcNow;

        public UpdateMetadataClient(GitHubRepository repository, ECDsa trustedKey)
            : this(repository, trustedKey, CreateHttpClient(), UpdateSource.CreateDefaults(), DefaultSourceTimeout,
                () => DateTimeOffset.UtcNow, true)
        {
        }

        internal UpdateMetadataClient(GitHubRepository repository, ECDsa trustedKey, HttpClient httpClient,
            IReadOnlyList<UpdateSource> sources, TimeSpan sourceTimeout, Func<DateTimeOffset> utcNow,
            bool ownsHttpClient = false)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _trustedKey = trustedKey ?? throw new ArgumentNullException(nameof(trustedKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _sources = sources == null || sources.Count == 0
                ? throw new ArgumentException("At least one update source is required.", nameof(sources))
                : sources;
            _sourceTimeout = sourceTimeout > TimeSpan.Zero
                ? sourceTimeout
                : throw new ArgumentOutOfRangeException(nameof(sourceTimeout));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _ownsHttpClient = ownsHttpClient;
        }

        public Task<UpdateMetadataResult> GetLatestAsync(CancellationToken cancellationToken)
        {
            return GetLatestAsync(cancellationToken, null);
        }

        public async Task<UpdateMetadataResult> GetLatestAsync(CancellationToken cancellationToken,
            NuGetVersion minimumVersion)
        {
            string targetUrl = BuildLatestMetadataUrl();
            var errors = new List<string>();
            foreach (UpdateSource source in _sources)
            {
                CandidateDownload download = await DownloadCandidateAsync(source, targetUrl,
                    cancellationToken).ConfigureAwait(false);
                if (download.Error != null)
                {
                    errors.Add(download.Source.Name + ": " + download.Error.Message);
                    continue;
                }

                try
                {
                    UpdateMetadata metadata = UpdateMetadataSignature.Verify(download.Json, _trustedKey);
                    if (!string.Equals(metadata.Repository, _repository.Slug, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The metadata repository does not match the application.");
                    if (minimumVersion != null && VersionComparer.VersionRelease.Compare(
                            ReleaseVersion.Parse(metadata.Version), minimumVersion) < 0)
                        throw new InvalidDataException("The mirror metadata is older than the known application version.");
                    return new UpdateMetadataResult(metadata, download.Source.Name,
                        download.ElapsedMilliseconds, 1);
                }
                catch (Exception exception) when (exception is InvalidDataException ||
                                                  exception is FormatException ||
                                                  exception is CryptographicException)
                {
                    errors.Add(download.Source.Name + ": " + exception.Message);
                }
            }

            throw new UpdateMetadataUnavailableException(errors);
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }

        private string BuildLatestMetadataUrl()
        {
            long cacheKey = _utcNow().ToUnixTimeSeconds() / (long)TimeSpan.FromMinutes(10).TotalSeconds;
            return $"https://github.com/{_repository.Slug}/releases/latest/download/" +
                   $"{UpdatePackageNaming.MetadataAssetName}?touchpilot_check={cacheKey}";
        }

        private async Task<CandidateDownload> DownloadCandidateAsync(UpdateSource source, string targetUrl,
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
                if (response.Content.Headers.ContentLength > MaximumMetadataBytes)
                    throw new InvalidDataException("The update metadata exceeds the size limit.");

                string json = await ReadLimitedStringAsync(response, timeout.Token).ConfigureAwait(false);
                return CandidateDownload.Success(source, json, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                return CandidateDownload.Failure(source,
                    new TimeoutException("The update source timed out.", exception), stopwatch.ElapsedMilliseconds);
            }
            catch (Exception exception) when (exception is HttpRequestException ||
                                              exception is IOException ||
                                              exception is InvalidDataException)
            {
                return CandidateDownload.Failure(source, exception, stopwatch.ElapsedMilliseconds);
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
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaximumMetadataBytes)
                    throw new InvalidDataException("The update metadata exceeds the size limit.");
                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return Encoding.UTF8.GetString(memory.ToArray());
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TouchPilot-Updater");
            return client;
        }

        private sealed class CandidateDownload
        {
            private CandidateDownload(UpdateSource source, string json, Exception error,
                long elapsedMilliseconds)
            {
                Source = source;
                Json = json;
                Error = error;
                ElapsedMilliseconds = elapsedMilliseconds;
            }

            public UpdateSource Source { get; }
            public string Json { get; }
            public Exception Error { get; }
            public long ElapsedMilliseconds { get; }

            public static CandidateDownload Success(UpdateSource source, string json, long elapsedMilliseconds)
            {
                return new CandidateDownload(source, json, null, elapsedMilliseconds);
            }

            public static CandidateDownload Failure(UpdateSource source, Exception error,
                long elapsedMilliseconds)
            {
                return new CandidateDownload(source, null, error, elapsedMilliseconds);
            }
        }

    }

    public sealed class UpdateSource
    {
        public UpdateSource(string name, string prefix)
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("An update source name is required.", nameof(name))
                : name;
            Prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        }

        public string Name { get; }

        public string Prefix { get; }

        public static IReadOnlyList<UpdateSource> CreateDefaults()
        {
            return new[]
            {
                new UpdateSource("gitwarp", "https://proxy.gitwarp.top/"),
                new UpdateSource("ghfast", "https://ghfast.top/"),
                new UpdateSource("gh-proxy", "https://gh-proxy.org/"),
                new UpdateSource("gh-proxy-v4", "https://v4.gh-proxy.org/"),
                new UpdateSource("gh-proxy-v6", "https://v6.gh-proxy.org/"),
                new UpdateSource("gh-proxy-cdn", "https://cdn.gh-proxy.org/"),
                new UpdateSource("direct", string.Empty)
            };
        }
    }

    public sealed class UpdateMetadataResult
    {
        internal UpdateMetadataResult(UpdateMetadata metadata, string sourceName, long elapsedMilliseconds,
            int validSourceCount)
        {
            Metadata = metadata;
            SourceName = sourceName;
            ElapsedMilliseconds = elapsedMilliseconds;
            ValidSourceCount = validSourceCount;
        }

        public UpdateMetadata Metadata { get; }
        public string SourceName { get; }
        public long ElapsedMilliseconds { get; }
        public int ValidSourceCount { get; }
    }

    public sealed class UpdateMetadataUnavailableException : Exception
    {
        public UpdateMetadataUnavailableException(IReadOnlyList<string> errors)
            : base("No trusted update metadata source was available. " +
                   string.Join(" | ", errors ?? Array.Empty<string>()))
        {
            Errors = errors ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> Errors { get; }
    }
}

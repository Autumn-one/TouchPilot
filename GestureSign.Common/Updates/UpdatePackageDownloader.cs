using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.Common.Updates
{
    public sealed class UpdatePackageDownloader : IDisposable
    {
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly IReadOnlyList<UpdateSource> _sources;

        public UpdatePackageDownloader()
            : this(CreateHttpClient(), UpdateSource.CreateDefaults(), true)
        {
        }

        internal UpdatePackageDownloader(HttpClient httpClient, IReadOnlyList<UpdateSource> sources,
            bool ownsHttpClient = false)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _sources = sources == null || sources.Count == 0
                ? throw new ArgumentException("At least one update source is required.", nameof(sources))
                : sources;
            _ownsHttpClient = ownsHttpClient;
        }

        public async Task<string> DownloadAsync(GitHubRepository repository, string tag,
            UpdateAssetMetadata asset, string destinationPath, IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            ValidateAsset(tag, asset, destinationPath);

            string originUrl = BuildAssetUrl(repository, tag, asset.Name);
            ProbeResult[] probes = await Task.WhenAll(_sources.Select(source =>
                ProbeAsync(source, originUrl, asset.Size, cancellationToken))).ConfigureAwait(false);
            UpdateSource[] candidates = probes.Where(probe => probe.Success)
                .OrderBy(probe => probe.ElapsedMilliseconds)
                .Select(probe => probe.Source)
                .ToArray();
            if (candidates.Length == 0)
                throw new HttpRequestException("No update download source passed the range probe.");

            string partialPath = destinationPath + ".download";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath)));
            if (File.Exists(partialPath) && new FileInfo(partialPath).Length > asset.Size)
                File.Delete(partialPath);

            var failures = new List<Exception>();
            foreach (UpdateSource source in candidates)
            {
                try
                {
                    await DownloadFromSourceAsync(source, originUrl, asset, partialPath, progress,
                        cancellationToken).ConfigureAwait(false);
                    ValidateCompletedFile(partialPath, asset);
                    File.Move(partialPath, destinationPath, true);
                    return destinationPath;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is HttpRequestException ||
                                                  exception is IOException ||
                                                  exception is InvalidDataException ||
                                                  exception is TimeoutException)
                {
                    failures.Add(exception);
                    if (File.Exists(partialPath) && new FileInfo(partialPath).Length > asset.Size)
                        File.Delete(partialPath);
                }
            }

            throw new AggregateException("Every update download source failed.", failures);
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }

        private async Task<ProbeResult> ProbeAsync(UpdateSource source, string originUrl, long expectedSize,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ProbeTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, source.Prefix + originUrl);
                request.Headers.Range = new RangeHeaderValue(0, 0);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                using HttpResponseMessage response = await _httpClient.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                bool valid = response.StatusCode == HttpStatusCode.PartialContent
                    ? response.Content.Headers.ContentRange?.Length == expectedSize
                    : response.IsSuccessStatusCode && response.Content.Headers.ContentLength == expectedSize;
                return new ProbeResult(source, valid, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ProbeResult(source, false, stopwatch.ElapsedMilliseconds);
            }
            catch (HttpRequestException)
            {
                return new ProbeResult(source, false, stopwatch.ElapsedMilliseconds);
            }
        }

        private async Task DownloadFromSourceAsync(UpdateSource source, string originUrl,
            UpdateAssetMetadata asset, string partialPath, IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            long existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Prefix + originUrl);
            if (existingLength > 0)
                request.Headers.Range = new RangeHeaderValue(existingLength, null);
            using HttpResponseMessage response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            bool append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent &&
                          response.Content.Headers.ContentRange?.From == existingLength &&
                          response.Content.Headers.ContentRange?.Length == asset.Size;
            if (!append)
            {
                existingLength = 0;
                if (response.StatusCode == HttpStatusCode.PartialContent &&
                    (response.Content.Headers.ContentRange?.From != 0 ||
                     response.Content.Headers.ContentRange?.Length != asset.Size))
                    throw new InvalidDataException("The update source returned an invalid byte range.");
                if (response.StatusCode != HttpStatusCode.PartialContent &&
                    response.Content.Headers.ContentLength != asset.Size)
                    throw new InvalidDataException("The update source returned an invalid package size.");
            }

            using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var destination = new FileStream(partialPath, append ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, true);
            byte[] buffer = new byte[81920];
            long total = existingLength;
            while (true)
            {
                int read = await ReadWithTimeoutAsync(sourceStream, buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                if (total > asset.Size)
                    throw new InvalidDataException("The update source returned more data than expected.");
                progress?.Report(total * 100d / asset.Size);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (total != asset.Size)
                throw new IOException("The update download ended before the package was complete.");
        }

        private static async Task<int> ReadWithTimeoutAsync(Stream stream, byte[] buffer,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReadTimeout);
            try
            {
                return await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The update download stopped receiving data.", exception);
            }
        }

        private static void ValidateCompletedFile(string path, UpdateAssetMetadata asset)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != asset.Size)
                throw new InvalidDataException("The downloaded update package has an invalid size.");
            using FileStream stream = File.OpenRead(path);
            using SHA256 sha256 = SHA256.Create();
            string actualHash = Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
            if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidDataException("The downloaded update package failed SHA-256 verification.");
            }
        }

        private static string BuildAssetUrl(GitHubRepository repository, string tag, string assetName)
        {
            return $"https://github.com/{repository.Slug}/releases/download/" +
                   $"{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(assetName)}";
        }

        private static void ValidateAsset(string tag, UpdateAssetMetadata asset, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith("v", StringComparison.Ordinal))
                throw new ArgumentException("A canonical release tag is required.", nameof(tag));
            if (asset == null || asset.Size <= 0 || string.IsNullOrWhiteSpace(asset.Name) ||
                !string.Equals(Path.GetFileName(asset.Name), asset.Name, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(asset.Sha256) || asset.Sha256.Length != 64 ||
                !asset.Sha256.All(Uri.IsHexDigit))
                throw new ArgumentException("The update asset is invalid.", nameof(asset));
            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("A download destination is required.", nameof(destinationPath));
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TouchPilot-Updater");
            return client;
        }

        private sealed class ProbeResult
        {
            public ProbeResult(UpdateSource source, bool success, long elapsedMilliseconds)
            {
                Source = source;
                Success = success;
                ElapsedMilliseconds = elapsedMilliseconds;
            }

            public UpdateSource Source { get; }
            public bool Success { get; }
            public long ElapsedMilliseconds { get; }
        }
    }
}

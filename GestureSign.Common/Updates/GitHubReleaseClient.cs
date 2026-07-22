using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.Common.Updates
{
    public sealed class GitHubReleaseClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly GitHubRepository _repository;

        public GitHubReleaseClient(GitHubRepository repository, string token = null)
            : this(repository, CreateHttpClient(token), true)
        {
        }

        internal GitHubReleaseClient(GitHubRepository repository, HttpClient httpClient, bool ownsHttpClient = false)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = ownsHttpClient;
        }

        public async Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                $"https://api.github.com/repos/{_repository.Owner}/{_repository.Name}/releases/latest",
                cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            GitHubReleaseDto release = JsonSerializer.Deserialize<GitHubReleaseDto>(json);
            if (release == null || string.IsNullOrWhiteSpace(release.tag_name))
                throw new InvalidDataException("GitHub returned an invalid release response.");

            return new GitHubReleaseInfo
            {
                Id = release.id,
                TagName = release.tag_name,
                Name = release.name,
                Body = release.body,
                HtmlUrl = release.html_url,
                PublishedAt = release.published_at,
                Assets = (release.assets ?? new List<GitHubReleaseAssetDto>()).Select(asset =>
                    new GitHubReleaseAsset
                    {
                        Id = asset.id,
                        Name = asset.name,
                        DownloadUrl = asset.browser_download_url,
                        Size = asset.size
                    }).ToArray()
            };
        }

        public async Task DownloadFileAsync(string url, string destinationPath, IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(url,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);

            long? contentLength = response.Content.Headers.ContentLength;
            using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using FileStream destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, true);

            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalRead += read;
                if (contentLength.GetValueOrDefault() > 0)
                    progress?.Report(totalRead * 100d / contentLength.Value);
            }
        }

        public async Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }

        private static HttpClient CreateHttpClient(string token)
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GestureSign-Updater");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            if (!string.IsNullOrWhiteSpace(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            return client;
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new HttpRequestException(
                $"GitHub request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). {body}",
                null, response.StatusCode);
        }

        private sealed class GitHubReleaseDto
        {
            public long id { get; set; }
            public string tag_name { get; set; }
            public string name { get; set; }
            public string body { get; set; }
            public string html_url { get; set; }
            public DateTimeOffset? published_at { get; set; }
            public List<GitHubReleaseAssetDto> assets { get; set; }
        }

        private sealed class GitHubReleaseAssetDto
        {
            public long id { get; set; }
            public string name { get; set; }
            public string browser_download_url { get; set; }
            public long size { get; set; }
        }
    }
}

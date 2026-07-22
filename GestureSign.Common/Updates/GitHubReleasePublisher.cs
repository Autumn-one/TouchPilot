using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.Common.Updates
{
    public sealed class GitHubReleasePublisher : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly GitHubRepository _repository;

        public GitHubReleasePublisher(GitHubRepository repository, string token)
            : this(repository ?? throw new ArgumentNullException(nameof(repository)),
                CreateHttpClient(token), true)
        {
        }

        internal GitHubReleasePublisher(GitHubRepository repository, HttpClient httpClient,
            bool ownsHttpClient = false)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = ownsHttpClient;
        }

        public async Task<GitHubReleaseInfo> PublishAsync(string tagName, string releaseName, string body,
            bool draft, bool prerelease, IReadOnlyList<string> assetPaths, IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            if (assetPaths == null || assetPaths.Count == 0)
                throw new ArgumentException("At least one release asset is required.", nameof(assetPaths));
            foreach (string path in assetPaths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("Release asset not found.", path);
            }

            GitHubReleaseInfo release = await GetReleaseByTagAsync(tagName, cancellationToken).ConfigureAwait(false);
            if (release == null)
            {
                progress?.Report("Creating GitHub release " + tagName + "...");
                release = await CreateReleaseAsync(tagName, releaseName, body, draft, prerelease,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                progress?.Report("Updating GitHub release " + tagName + "...");
                release = await UpdateReleaseAsync(release.Id, tagName, releaseName, body, draft, prerelease,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (string path in assetPaths)
            {
                string assetName = Path.GetFileName(path);
                GitHubReleaseAsset existing = release.Assets.FirstOrDefault(asset =>
                    string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    progress?.Report("Replacing existing asset " + assetName + "...");
                    await DeleteAssetAsync(existing.Id, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report("Uploading " + assetName + "...");
                await UploadAssetAsync(release.Id, path, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report("GitHub release published: " + release.HtmlUrl);
            return release;
        }

        public async Task<GitHubReleaseInfo> DeleteAsync(string tagName, IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException("A release tag is required.", nameof(tagName));
            tagName = tagName.Trim();

            GitHubReleaseInfo release = await GetReleaseByTagAsync(tagName, cancellationToken)
                .ConfigureAwait(false);
            if (release == null)
                throw new InvalidOperationException("GitHub release " + tagName + " was not found.");

            progress?.Report("Deleting GitHub release " + tagName + "...");
            await DeleteReleaseAsync(release.Id, cancellationToken).ConfigureAwait(false);
            progress?.Report("GitHub release deleted: " + tagName);
            return release;
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }

        private async Task<GitHubReleaseInfo> GetReleaseByTagAsync(string tagName,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                ApiUrl("releases/tags/" + Uri.EscapeDataString(tagName)), cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            return await ReadReleaseAsync(response, cancellationToken).ConfigureAwait(false);
        }

        private async Task<GitHubReleaseInfo> CreateReleaseAsync(string tagName, string releaseName, string body,
            bool draft, bool prerelease, CancellationToken cancellationToken)
        {
            var payload = new ReleasePayload
            {
                tag_name = tagName,
                name = releaseName,
                body = body ?? string.Empty,
                draft = draft,
                prerelease = prerelease
            };
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(ApiUrl("releases"), payload,
                cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            return await ReadReleaseAsync(response, cancellationToken).ConfigureAwait(false);
        }

        private async Task<GitHubReleaseInfo> UpdateReleaseAsync(long releaseId, string tagName,
            string releaseName, string body, bool draft, bool prerelease, CancellationToken cancellationToken)
        {
            var payload = new ReleasePayload
            {
                tag_name = tagName,
                name = releaseName,
                body = body ?? string.Empty,
                draft = draft,
                prerelease = prerelease
            };
            using var request = new HttpRequestMessage(HttpMethod.Patch, ApiUrl("releases/" + releaseId))
            {
                Content = JsonContent.Create(payload)
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            return await ReadReleaseAsync(response, cancellationToken).ConfigureAwait(false);
        }

        private async Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _httpClient.DeleteAsync(
                ApiUrl("releases/assets/" + assetId), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
        }

        private async Task DeleteReleaseAsync(long releaseId, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _httpClient.DeleteAsync(
                ApiUrl("releases/" + releaseId), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
        }

        private async Task UploadAssetAsync(long releaseId, string path, CancellationToken cancellationToken)
        {
            string assetName = Path.GetFileName(path);
            string url = $"https://uploads.github.com/repos/{_repository.Owner}/{_repository.Name}/releases/" +
                         $"{releaseId}/assets?name={Uri.EscapeDataString(assetName)}";
            using FileStream stream = File.OpenRead(path);
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(
                string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)
                    ? "application/zip"
                    : "text/plain");
            using HttpResponseMessage response = await _httpClient.PostAsync(url, content, cancellationToken)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
        }

        private string ApiUrl(string suffix)
        {
            return $"https://api.github.com/repos/{_repository.Owner}/{_repository.Name}/{suffix}";
        }

        private static HttpClient CreateHttpClient(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("A GitHub personal access token is required.", nameof(token));

            var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GestureSign-ReleaseManager");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.Trim());
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }

        private static async Task<GitHubReleaseInfo> ReadReleaseAsync(HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ReleaseDto dto = JsonSerializer.Deserialize<ReleaseDto>(json);
            if (dto == null || dto.id <= 0)
                throw new InvalidDataException("GitHub returned an invalid release response.");

            return new GitHubReleaseInfo
            {
                Id = dto.id,
                TagName = dto.tag_name,
                Name = dto.name,
                Body = dto.body,
                HtmlUrl = dto.html_url,
                PublishedAt = dto.published_at,
                Assets = (dto.assets ?? new List<AssetDto>()).Select(asset => new GitHubReleaseAsset
                {
                    Id = asset.id,
                    Name = asset.name,
                    DownloadUrl = asset.browser_download_url,
                    Size = asset.size
                }).ToArray()
            };
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

        private sealed class ReleasePayload
        {
            public string tag_name { get; set; }
            public string name { get; set; }
            public string body { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
        }

        private sealed class ReleaseDto
        {
            public long id { get; set; }
            public string tag_name { get; set; }
            public string name { get; set; }
            public string body { get; set; }
            public string html_url { get; set; }
            public DateTimeOffset? published_at { get; set; }
            public List<AssetDto> assets { get; set; }
        }

        private sealed class AssetDto
        {
            public long id { get; set; }
            public string name { get; set; }
            public string browser_download_url { get; set; }
            public long size { get; set; }
        }
    }
}

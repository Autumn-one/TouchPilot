using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.Common.Updates
{
    public sealed class GitHubReleasePublisher : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly ECDsa _trustedKey;
        private readonly bool _ownsTrustedKey;
        private readonly GitHubRepository _repository;

        public GitHubReleasePublisher(GitHubRepository repository, string token)
            : this(repository ?? throw new ArgumentNullException(nameof(repository)),
                CreateHttpClient(token), TrustedUpdateSigningKey.Load(), true, true)
        {
        }

        internal GitHubReleasePublisher(GitHubRepository repository, HttpClient httpClient,
            bool ownsHttpClient = false)
            : this(repository, httpClient, TrustedUpdateSigningKey.Load(), ownsHttpClient, true)
        {
        }

        internal GitHubReleasePublisher(GitHubRepository repository, HttpClient httpClient,
            ECDsa trustedKey, bool ownsHttpClient = false)
            : this(repository, httpClient, trustedKey, ownsHttpClient, false)
        {
        }

        private GitHubReleasePublisher(GitHubRepository repository, HttpClient httpClient,
            ECDsa trustedKey, bool ownsHttpClient, bool ownsTrustedKey)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _trustedKey = trustedKey ?? throw new ArgumentNullException(nameof(trustedKey));
            _ownsHttpClient = ownsHttpClient;
            _ownsTrustedKey = ownsTrustedKey;
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

        public async Task<GitHubReleaseInfo> EnsureCanPublishAsync(string tagName,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException("A release tag is required.", nameof(tagName));

            GitHubReleaseInfo release = await GetReleaseByTagAsync(tagName.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (release != null && !release.Draft)
                throw new InvalidOperationException(
                    "Published release " + tagName.Trim() + " is immutable and cannot be rebuilt.");
            return release;
        }

        public async Task<GitHubReleaseInfo> PublishImmutableAsync(string tagName, string releaseName,
            string body, bool draft, IReadOnlyList<string> assetPaths, IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            string[] orderedAssetPaths = ValidateImmutableAssets(tagName, assetPaths);
            tagName = tagName.Trim();
            GitHubReleaseInfo release = await EnsureCanPublishAsync(tagName, cancellationToken)
                .ConfigureAwait(false);
            if (release == null)
            {
                progress?.Report("Creating draft GitHub release " + tagName + "...");
                release = await CreateReleaseAsync(tagName, releaseName, body, true, false,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                progress?.Report("Rebuilding draft GitHub release " + tagName + "...");
            }

            if (!release.Draft)
                throw new InvalidOperationException(
                    "Published release " + tagName + " is immutable and cannot be rebuilt.");

            foreach (GitHubReleaseAsset existing in release.Assets)
            {
                progress?.Report("Removing draft asset " + existing.Name + "...");
                await DeleteAssetAsync(existing.Id, cancellationToken).ConfigureAwait(false);
            }

            foreach (string path in orderedAssetPaths)
            {
                progress?.Report("Uploading " + Path.GetFileName(path) + "...");
                await UploadAssetAsync(release.Id, path, cancellationToken).ConfigureAwait(false);
            }

            GitHubReleaseInfo verified = await GetReleaseByTagAsync(tagName, cancellationToken)
                .ConfigureAwait(false);
            ValidateUploadedAssets(verified, orderedAssetPaths);
            if (!verified.Draft)
                throw new InvalidOperationException(
                    "The GitHub release left draft state before asset verification completed.");

            GitHubReleaseInfo completed = await UpdateReleaseAsync(release.Id, tagName, releaseName,
                body, draft, false, cancellationToken).ConfigureAwait(false);
            if (completed.Draft != draft || completed.Prerelease)
                throw new InvalidDataException("GitHub returned an unexpected final release state.");
            ValidateUploadedAssets(completed, orderedAssetPaths);

            progress?.Report((draft ? "GitHub release draft ready: " : "GitHub release published: ") +
                             completed.HtmlUrl);
            return completed;
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
            if (_ownsTrustedKey)
                _trustedKey.Dispose();
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
            content.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(path));
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
                Draft = dto.draft,
                Prerelease = dto.prerelease,
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

        private string[] ValidateImmutableAssets(string tagName, IReadOnlyList<string> assetPaths)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException("A release tag is required.", nameof(tagName));
            string version = ReleaseVersion.ToReleaseString(ReleaseVersion.Parse(tagName));
            if (!string.Equals(tagName.Trim(), "v" + version, StringComparison.Ordinal))
                throw new InvalidDataException("The release tag must use canonical v-prefixed SemVer.");
            if (assetPaths == null)
                throw new ArgumentNullException(nameof(assetPaths));

            string[] expectedNames =
            {
                UpdatePackageNaming.GetInstallerAssetName(version),
                UpdatePackageNaming.GetPortableAssetName(version),
                UpdatePackageNaming.MetadataAssetName
            };
            var pathsByName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in assetPaths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("Release asset not found.", path);
                if (new FileInfo(path).Length <= 0)
                    throw new InvalidDataException("Release asset is empty: " + Path.GetFileName(path) + ".");
                string name = Path.GetFileName(path);
                if (!pathsByName.TryAdd(name, path))
                    throw new InvalidDataException("The release contains duplicate asset names.");
            }
            if (pathsByName.Count != expectedNames.Length ||
                expectedNames.Any(name => !pathsByName.ContainsKey(name)))
                throw new InvalidDataException(
                    "A release must contain the installer, portable ZIP, and signed update metadata.");

            string metadataPath = pathsByName[UpdatePackageNaming.MetadataAssetName];
            if (new FileInfo(metadataPath).Length > 64 * 1024)
                throw new InvalidDataException("The signed update metadata exceeds the client limit.");
            UpdateMetadata metadata = UpdateMetadataSignature.Verify(File.ReadAllText(metadataPath), _trustedKey);
            if (!string.Equals(metadata.Version, version, StringComparison.Ordinal) ||
                !string.Equals(metadata.Tag, tagName.Trim(), StringComparison.Ordinal) ||
                !string.Equals(metadata.Repository, _repository.Slug, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The signed update metadata does not match the release.");

            ValidateMetadataAsset(metadata, UpdatePackageNaming.InstallerDistribution,
                pathsByName[expectedNames[0]]);
            ValidateMetadataAsset(metadata, UpdatePackageNaming.PortableDistribution,
                pathsByName[expectedNames[1]]);
            return expectedNames.Select(name => pathsByName[name]).ToArray();
        }

        private static void ValidateMetadataAsset(UpdateMetadata metadata, string distribution,
            string path)
        {
            UpdateAssetMetadata asset = metadata.Assets.Single(item =>
                string.Equals(item.Distribution, distribution, StringComparison.Ordinal));
            var file = new FileInfo(path);
            if (!string.Equals(asset.Name, file.Name, StringComparison.Ordinal) ||
                asset.Size != file.Length ||
                !string.Equals(asset.Sha256, ComputeSha256(path), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The signed metadata does not match release asset " + file.Name + ".");
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }

        private static void ValidateUploadedAssets(GitHubReleaseInfo release,
            IReadOnlyList<string> expectedPaths)
        {
            if (release == null)
                throw new InvalidDataException("The GitHub release could not be reloaded after upload.");
            if (release.Assets.Count != expectedPaths.Count)
                throw new InvalidDataException("The GitHub release asset count is invalid.");

            foreach (string path in expectedPaths)
            {
                string name = Path.GetFileName(path);
                long size = new FileInfo(path).Length;
                GitHubReleaseAsset asset = release.Assets.SingleOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.Ordinal));
                if (asset == null || asset.Size != size)
                    throw new InvalidDataException("The uploaded release asset is invalid: " + name + ".");
            }
        }

        private static string GetContentType(string path)
        {
            string extension = Path.GetExtension(path);
            if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
                return "application/zip";
            if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
                return "application/json";
            if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                return "application/vnd.microsoft.portable-executable";
            return "application/octet-stream";
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
            public bool draft { get; set; }
            public bool prerelease { get; set; }
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

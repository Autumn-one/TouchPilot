using GestureSign.Common.Updates;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GestureSign.Tests
{
    public class GitHubReleasePublisherTests
    {
        [Fact]
        public async Task ImmutablePublishUploadsVerifiedAssetsBeforePublishing()
        {
            using var directory = new PublisherTemporaryDirectory();
            const string version = "8.3.0-beta.4";
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            IReadOnlyList<string> assets = directory.CreateAssets(version, signingKey);
            var handler = new TransactionalReleaseHandler(assets);
            using var httpClient = new HttpClient(handler);
            using var publisher = new GitHubReleasePublisher(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), httpClient, signingKey);

            GitHubReleaseInfo release = await publisher.PublishImmutableAsync("v" + version,
                "TouchPilot " + version, "notes", false, assets, null, CancellationToken.None);

            Assert.False(release.Draft);
            Assert.False(release.Prerelease);
            Assert.Equal(3, release.Assets.Count);
            Assert.Equal("GET release by tag", handler.Events[0]);
            Assert.Equal("LIST releases", handler.Events[1]);
            Assert.Equal("CREATE draft", handler.Events[2]);
            Assert.Equal(new[]
            {
                "application/vnd.microsoft.portable-executable",
                "application/zip",
                "application/json"
            }, handler.UploadContentTypes);
            Assert.Equal("VERIFY draft", handler.Events[^2]);
            Assert.Equal("PUBLISH", handler.Events[^1]);
            Assert.All(handler.ReleasePayloads, payload => Assert.False(payload.Prerelease));
            Assert.True(handler.ReleasePayloads[0].Draft);
            Assert.False(handler.ReleasePayloads[^1].Draft);
        }

        [Fact]
        public async Task ImmutablePublishRejectsAnExistingPublishedVersionBeforeMutation()
        {
            using var directory = new PublisherTemporaryDirectory();
            const string version = "8.3.0-beta.4";
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            IReadOnlyList<string> assets = directory.CreateAssets(version, signingKey);
            var handler = new PublishedReleaseHandler();
            using var httpClient = new HttpClient(handler);
            using var publisher = new GitHubReleasePublisher(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), httpClient, signingKey);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                publisher.PublishImmutableAsync("v" + version, "TouchPilot " + version,
                    string.Empty, false, assets, null, CancellationToken.None));

            Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Get, handler.Requests[0]);
        }

        [Fact]
        public async Task ImmutablePublishRejectsIncompleteAssetSetsWithoutNetworkAccess()
        {
            using var directory = new PublisherTemporaryDirectory();
            const string version = "8.3.0-beta.4";
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            IReadOnlyList<string> assets = directory.CreateAssets(version, signingKey).Take(2).ToArray();
            var handler = new PublishedReleaseHandler();
            using var httpClient = new HttpClient(handler);
            using var publisher = new GitHubReleasePublisher(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), httpClient, signingKey);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                publisher.PublishImmutableAsync("v" + version, "TouchPilot " + version,
                    string.Empty, false, assets, null, CancellationToken.None));

            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task ImmutablePublishRejectsTamperedMetadataWithoutNetworkAccess()
        {
            using var directory = new PublisherTemporaryDirectory();
            const string version = "8.3.0-beta.4";
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            IReadOnlyList<string> assets = directory.CreateAssets(version, signingKey);
            string metadataPath = assets.Single(path =>
                Path.GetFileName(path) == UpdatePackageNaming.MetadataAssetName);
            File.WriteAllText(metadataPath, File.ReadAllText(metadataPath)
                .Replace(version, "8.3.0-beta.5"));
            var handler = new PublishedReleaseHandler();
            using var httpClient = new HttpClient(handler);
            using var publisher = new GitHubReleasePublisher(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), httpClient, signingKey);

            await Assert.ThrowsAsync<CryptographicException>(() =>
                publisher.PublishImmutableAsync("v" + version, "TouchPilot " + version,
                    string.Empty, false, assets, null, CancellationToken.None));

            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task ImmutablePublishRebuildsDraftAndRemovesStaleAssets()
        {
            using var directory = new PublisherTemporaryDirectory();
            const string version = "8.3.0-beta.4";
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            IReadOnlyList<string> assets = directory.CreateAssets(version, signingKey);
            var handler = new DraftRebuildHandler(assets);
            using var httpClient = new HttpClient(handler);
            using var publisher = new GitHubReleasePublisher(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), httpClient, signingKey);

            GitHubReleaseInfo release = await publisher.PublishImmutableAsync("v" + version,
                "TouchPilot " + version, "updated notes", true, assets, null,
                CancellationToken.None);

            Assert.True(release.Draft);
            Assert.False(release.Prerelease);
            Assert.Equal("GET draft by tag", handler.Events[0]);
            Assert.Equal("LIST drafts", handler.Events[1]);
            Assert.Equal("DELETE stale", handler.Events[2]);
            Assert.Equal(3, handler.Events.Count(item => item == "UPLOAD"));
            Assert.Equal("VERIFY draft", handler.Events[^2]);
            Assert.Equal("KEEP DRAFT", handler.Events[^1]);
        }

        private sealed class TransactionalReleaseHandler : HttpMessageHandler
        {
            private readonly IReadOnlyDictionary<string, long> _assets;

            public TransactionalReleaseHandler(IReadOnlyList<string> assetPaths)
            {
                _assets = assetPaths.ToDictionary(Path.GetFileName,
                    path => new FileInfo(path).Length, StringComparer.Ordinal);
            }

            public List<string> Events { get; } = new List<string>();
            public List<string> UploadContentTypes { get; } = new List<string>();
            public List<(bool Draft, bool Prerelease)> ReleasePayloads { get; } =
                new List<(bool Draft, bool Prerelease)>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                string url = request.RequestUri.ToString();
                if (request.Method == HttpMethod.Get && url.Contains("/releases/tags/"))
                {
                    Events.Add("GET release by tag");
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                if (request.Method == HttpMethod.Get && url.Contains("/releases?"))
                {
                    Events.Add("LIST releases");
                    return JsonResponse("[]");
                }

                if (request.Method == HttpMethod.Get && url.EndsWith("/releases/42"))
                {
                    Events.Add("VERIFY draft");
                    return JsonResponse(CreateReleaseJson(true));
                }

                if (request.Method == HttpMethod.Post && url.EndsWith("/releases"))
                {
                    (bool draft, bool prerelease) = await ReadReleaseStateAsync(request);
                    ReleasePayloads.Add((draft, prerelease));
                    Events.Add("CREATE draft");
                    return JsonResponse(CreateReleaseJson(true, includeAssets: false));
                }

                if (request.Method == HttpMethod.Post && url.Contains("uploads.github.com"))
                {
                    Events.Add("UPLOAD");
                    UploadContentTypes.Add(request.Content.Headers.ContentType.MediaType);
                    return JsonResponse("{}");
                }

                if (request.Method.Method == "PATCH" && url.Contains("/releases/42"))
                {
                    (bool draft, bool prerelease) = await ReadReleaseStateAsync(request);
                    ReleasePayloads.Add((draft, prerelease));
                    Events.Add(draft ? "KEEP DRAFT" : "PUBLISH");
                    return JsonResponse(CreateReleaseJson(draft));
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            private string CreateReleaseJson(bool draft, bool includeAssets = true)
            {
                object[] assets = includeAssets
                    ? _assets.Select((asset, index) => new
                    {
                        id = index + 100,
                        name = asset.Key,
                        browser_download_url = "https://example.invalid/" + asset.Key,
                        size = asset.Value
                    }).Cast<object>().ToArray()
                    : Array.Empty<object>();
                return JsonSerializer.Serialize(new
                {
                    id = 42,
                    tag_name = "v8.3.0-beta.4",
                    name = "TouchPilot 8.3.0-beta.4",
                    body = "notes",
                    html_url = "https://github.com/Autumn-one/TouchPilot/releases/tag/v8.3.0-beta.4",
                    draft,
                    prerelease = false,
                    assets
                });
            }

            private static async Task<(bool Draft, bool Prerelease)> ReadReleaseStateAsync(
                HttpRequestMessage request)
            {
                string json = await request.Content.ReadAsStringAsync();
                using JsonDocument document = JsonDocument.Parse(json);
                return (document.RootElement.GetProperty("draft").GetBoolean(),
                    document.RootElement.GetProperty("prerelease").GetBoolean());
            }
        }

        private sealed class PublishedReleaseHandler : HttpMessageHandler
        {
            public List<HttpMethod> Requests { get; } = new List<HttpMethod>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request.Method);
                const string json =
                    "{\"id\":42,\"tag_name\":\"v8.3.0-beta.4\"," +
                    "\"name\":\"TouchPilot 8.3.0-beta.4\",\"body\":\"\"," +
                    "\"html_url\":\"https://example.invalid/release\"," +
                    "\"draft\":false,\"prerelease\":false,\"assets\":[]}";
                return Task.FromResult(JsonResponse(json));
            }
        }

        private sealed class DraftRebuildHandler : HttpMessageHandler
        {
            private readonly IReadOnlyDictionary<string, long> _assets;

            public DraftRebuildHandler(IReadOnlyList<string> assetPaths)
            {
                _assets = assetPaths.ToDictionary(Path.GetFileName,
                    path => new FileInfo(path).Length, StringComparer.Ordinal);
            }

            public List<string> Events { get; } = new List<string>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                string url = request.RequestUri.ToString();
                if (request.Method == HttpMethod.Get && url.Contains("/releases/tags/"))
                {
                    Events.Add("GET draft by tag");
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                if (request.Method == HttpMethod.Get && url.Contains("/releases?"))
                {
                    Events.Add("LIST drafts");
                    return JsonResponse("[" + CreateReleaseJson(includeStaleAsset: true) + "]");
                }
                if (request.Method == HttpMethod.Get && url.EndsWith("/releases/42"))
                {
                    Events.Add("VERIFY draft");
                    return JsonResponse(CreateReleaseJson(includeStaleAsset: false));
                }
                if (request.Method == HttpMethod.Delete && url.EndsWith("/releases/assets/9"))
                {
                    Events.Add("DELETE stale");
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (request.Method == HttpMethod.Post && url.Contains("uploads.github.com"))
                {
                    Events.Add("UPLOAD");
                    return JsonResponse("{}");
                }
                if (request.Method.Method == "PATCH" && url.Contains("/releases/42"))
                {
                    string json = await request.Content.ReadAsStringAsync();
                    using JsonDocument document = JsonDocument.Parse(json);
                    Assert.True(document.RootElement.GetProperty("draft").GetBoolean());
                    Assert.False(document.RootElement.GetProperty("prerelease").GetBoolean());
                    Events.Add("KEEP DRAFT");
                    return JsonResponse(CreateReleaseJson(includeStaleAsset: false));
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            private string CreateReleaseJson(bool includeStaleAsset)
            {
                object[] assets = includeStaleAsset
                    ? new object[] { new { id = 9, name = "stale.bin", size = 1 } }
                    : _assets.Select((asset, index) => new
                    {
                        id = index + 100,
                        name = asset.Key,
                        browser_download_url = "https://example.invalid/" + asset.Key,
                        size = asset.Value
                    }).Cast<object>().ToArray();
                return JsonSerializer.Serialize(new
                {
                    id = 42,
                    tag_name = "v8.3.0-beta.4",
                    name = "TouchPilot 8.3.0-beta.4",
                    body = "updated notes",
                    html_url = "https://example.invalid/release",
                    draft = true,
                    prerelease = false,
                    assets
                });
            }
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private sealed class PublisherTemporaryDirectory : IDisposable
        {
            public PublisherTemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "TouchPilot.PublisherTests." + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public IReadOnlyList<string> CreateAssets(string version, ECDsa signingKey)
            {
                string installerName = UpdatePackageNaming.GetInstallerAssetName(version);
                string portableName = UpdatePackageNaming.GetPortableAssetName(version);
                string installerPath = System.IO.Path.Combine(Path, installerName);
                string portablePath = System.IO.Path.Combine(Path, portableName);
                File.WriteAllText(installerPath, "asset " + installerName);
                File.WriteAllText(portablePath, "asset " + portableName);
                DateTimeOffset builtAt = new DateTimeOffset(2026, 7, 23, 7, 0, 0, TimeSpan.Zero);
                var metadata = new UpdateMetadata
                {
                    Repository = "Autumn-one/TouchPilot",
                    Version = version,
                    Tag = "v" + version,
                    BuiltAtUtc = builtAt,
                    ExpiresAtUtc = builtAt.AddMonths(3),
                    Assets = new List<UpdateAssetMetadata>
                    {
                        CreateMetadataAsset(UpdatePackageNaming.InstallerDistribution,
                            installerName, installerPath),
                        CreateMetadataAsset(UpdatePackageNaming.PortableDistribution,
                            portableName, portablePath)
                    }
                };
                string metadataPath = System.IO.Path.Combine(Path,
                    UpdatePackageNaming.MetadataAssetName);
                File.WriteAllText(metadataPath, UpdateMetadataSignature.Sign(metadata, signingKey));
                string[] names = { installerName, portableName, UpdatePackageNaming.MetadataAssetName };
                return names.Select(name => System.IO.Path.Combine(Path, name)).ToArray();
            }

            private static UpdateAssetMetadata CreateMetadataAsset(string distribution, string name,
                string path)
            {
                return new UpdateAssetMetadata
                {
                    Distribution = distribution,
                    Runtime = UpdatePackageNaming.WindowsX64Runtime,
                    Name = name,
                    Size = new FileInfo(path).Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
                };
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, true);
                }
                catch
                {
                }
            }
        }
    }
}

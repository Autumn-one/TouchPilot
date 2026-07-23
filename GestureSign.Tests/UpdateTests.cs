using GestureSign.Common.Updates;
using GestureSign.Updater;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using Xunit;

namespace GestureSign.Tests
{
    public class UpdateTests
    {
        [Theory]
        [InlineData("TransposonY/GestureSign", "TransposonY/GestureSign")]
        [InlineData("https://github.com/TransposonY/GestureSign", "TransposonY/GestureSign")]
        [InlineData("https://www.github.com/TransposonY/GestureSign.git/", "TransposonY/GestureSign")]
        public void GitHubRepositoryParsesSlugAndUrl(string value, string expectedSlug)
        {
            Assert.Equal(expectedSlug, GitHubRepository.Parse(value).Slug);
        }

        [Theory]
        [InlineData("")]
        [InlineData("owner")]
        [InlineData("https://example.com/owner/repository")]
        [InlineData("owner/repository/releases")]
        public void GitHubRepositoryRejectsInvalidValues(string value)
        {
            Assert.False(GitHubRepository.TryParse(value, out _));
        }

        [Fact]
        public void ReleaseVersionPreservesSemVerAndComparesReleaseOrder()
        {
            NuGetVersion current = ReleaseVersion.Parse("8.1.9");
            NuGetVersion beta1 = ReleaseVersion.Parse("v8.2.0-beta.1+build.7");
            NuGetVersion beta2 = ReleaseVersion.Parse("8.2.0-beta.2");
            NuGetVersion release = ReleaseVersion.Parse("8.2.0");
            NuGetVersion nextBeta = ReleaseVersion.Parse("8.3.0-beta.1");

            Assert.True(VersionComparer.VersionRelease.Compare(beta1, current) > 0);
            Assert.True(VersionComparer.VersionRelease.Compare(beta2, beta1) > 0);
            Assert.True(VersionComparer.VersionRelease.Compare(release, beta2) > 0);
            Assert.True(VersionComparer.VersionRelease.Compare(nextBeta, release) > 0);
            Assert.Equal("8.2.0-beta.1+build.7", ReleaseVersion.ToReleaseString(beta1));
        }

        [Fact]
        public void UpdatePackageNamingFindsRuntimeAssetCaseInsensitively()
        {
            var release = new GitHubReleaseInfo
            {
                Assets = new List<GitHubReleaseAsset>
                {
                    new GitHubReleaseAsset { Name = "gesturesign-8.2.0-WIN-X64.ZIP" }
                }
            };

            string name = UpdatePackageNaming.GetAssetName("8.2.0", "win-x64");

            Assert.Equal("GestureSign-8.2.0-win-x64.zip", name);
            Assert.NotNull(UpdatePackageNaming.FindAsset(release, name));
            Assert.Equal(name + ".sha256",
                UpdatePackageNaming.GetChecksumAssetName("8.2.0", "win-x64"));
        }

        [Fact]
        public void UpdateMetadataSignatureRoundTripsInstallerAndPortableAssets()
        {
            DateTimeOffset builtAt = new DateTimeOffset(2026, 7, 23, 4, 5, 6, TimeSpan.Zero);
            UpdateMetadata metadata = CreateUpdateMetadata(builtAt);
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string json = UpdateMetadataSignature.Sign(metadata, signingKey);

            UpdateMetadata verified = UpdateMetadataSignature.Verify(json, signingKey);

            Assert.Equal("8.3.0-beta.1", verified.Version);
            Assert.Equal(builtAt.AddMonths(3), verified.ExpiresAtUtc);
            Assert.Equal(UpdatePackageNaming.GetInstallerAssetName(verified.Version),
                verified.Assets[0].Name);
            Assert.Equal(UpdatePackageNaming.GetPortableAssetName(verified.Version),
                verified.Assets[1].Name);
        }

        [Fact]
        public void UpdateMetadataSignatureRejectsTampering()
        {
            UpdateMetadata metadata = CreateUpdateMetadata(DateTimeOffset.UtcNow);
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string json = UpdateMetadataSignature.Sign(metadata, signingKey);
            string tampered = json.Replace("8.3.0-beta.1", "8.3.0-beta.2");

            Assert.Throws<CryptographicException>(() =>
                UpdateMetadataSignature.Verify(tampered, signingKey));
        }

        [Fact]
        public void UpdateMetadataRejectsExpiryOtherThanThreeCalendarMonths()
        {
            UpdateMetadata metadata = CreateUpdateMetadata(DateTimeOffset.UtcNow);
            metadata.ExpiresAtUtc = metadata.BuiltAtUtc.AddDays(90);
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            Assert.Throws<InvalidDataException>(() =>
                UpdateMetadataSignature.Sign(metadata, signingKey));
        }

        [Fact]
        public async Task MetadataClientSelectsHighestTrustedVersionAcrossProxies()
        {
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string stale = UpdateMetadataSignature.Sign(
                CreateUpdateMetadata(DateTimeOffset.UtcNow.AddDays(-1), "8.3.0-beta.1"), signingKey);
            string current = UpdateMetadataSignature.Sign(
                CreateUpdateMetadata(DateTimeOffset.UtcNow, "8.3.0-beta.2"), signingKey);
            var handler = new MetadataHandler(new Dictionary<string, string>
            {
                ["stale.invalid"] = stale,
                ["current.invalid"] = current
            });
            using var httpClient = new HttpClient(handler);
            using var client = new UpdateMetadataClient(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), signingKey, httpClient,
                new[]
                {
                    new UpdateSource("stale", "https://stale.invalid/"),
                    new UpdateSource("current", "https://current.invalid/")
                }, TimeSpan.FromSeconds(1), () => DateTimeOffset.UnixEpoch);

            UpdateMetadataResult result = await client.GetLatestAsync(CancellationToken.None);

            Assert.Equal("8.3.0-beta.2", result.Metadata.Version);
            Assert.Equal("current", result.SourceName);
            Assert.Equal(2, result.ValidSourceCount);
            Assert.All(handler.Requests, request =>
            {
                Assert.Contains("/releases/latest/download/TouchPilot-update.json", request.Url);
                Assert.True(request.NoCache);
                Assert.Null(request.Authorization);
            });
        }

        [Fact]
        public async Task MetadataClientRejectsUnsignedProxyResponse()
        {
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using ECDsa untrustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string untrusted = UpdateMetadataSignature.Sign(
                CreateUpdateMetadata(DateTimeOffset.UtcNow, "99.0.0"), untrustedKey);
            var handler = new MetadataHandler(new Dictionary<string, string>
            {
                ["untrusted.invalid"] = untrusted
            });
            using var httpClient = new HttpClient(handler);
            using var client = new UpdateMetadataClient(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), signingKey, httpClient,
                new[] { new UpdateSource("untrusted", "https://untrusted.invalid/") },
                TimeSpan.FromSeconds(1), () => DateTimeOffset.UnixEpoch);

            await Assert.ThrowsAsync<UpdateMetadataUnavailableException>(() =>
                client.GetLatestAsync(CancellationToken.None));
        }

        [Fact]
        public async Task MetadataClientRejectsOversizedProxyResponse()
        {
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var handler = new MetadataHandler(new Dictionary<string, string>
            {
                ["oversized.invalid"] = new string('x', 65 * 1024)
            });
            using var httpClient = new HttpClient(handler);
            using var client = new UpdateMetadataClient(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), signingKey, httpClient,
                new[] { new UpdateSource("oversized", "https://oversized.invalid/") },
                TimeSpan.FromSeconds(1), () => DateTimeOffset.UnixEpoch);

            UpdateMetadataUnavailableException exception =
                await Assert.ThrowsAsync<UpdateMetadataUnavailableException>(() =>
                    client.GetLatestAsync(CancellationToken.None));

            Assert.Contains("size limit", exception.Message);
        }

        [Fact]
        public void MandatoryUpdatePolicyPreservesFirstDetectionAcrossNewerReleases()
        {
            var state = new MandatoryUpdateState();
            DateTimeOffset firstSeen = new DateTimeOffset(2026, 7, 23, 1, 0, 0, TimeSpan.Zero);

            MandatoryUpdatePolicy.RecordSuccessfulCheck(state, ReleaseVersion.Parse("8.2.0"),
                ReleaseVersion.Parse("8.3.0-beta.1"), firstSeen);
            MandatoryUpdateDecision decision = MandatoryUpdatePolicy.RecordSuccessfulCheck(state,
                ReleaseVersion.Parse("8.2.0"), ReleaseVersion.Parse("8.3.0-beta.2"),
                firstSeen.AddDays(2));

            Assert.Equal(firstSeen, state.FirstSeenUtc);
            Assert.Equal("8.3.0-beta.2", state.PendingVersion);
            Assert.False(decision.MustUpdate);
            Assert.Equal(TimeSpan.FromDays(1), decision.Remaining);
        }

        [Fact]
        public void MandatoryUpdatePolicyBlocksAfterThreeDaysAndResistsClockRollback()
        {
            var state = new MandatoryUpdateState();
            DateTimeOffset firstSeen = new DateTimeOffset(2026, 7, 23, 1, 0, 0, TimeSpan.Zero);
            MandatoryUpdatePolicy.RecordSuccessfulCheck(state, ReleaseVersion.Parse("8.2.0"),
                ReleaseVersion.Parse("8.3.0-beta.1"), firstSeen);

            MandatoryUpdateDecision expired = MandatoryUpdatePolicy.ObserveOffline(state,
                firstSeen.AddDays(3).AddSeconds(1));
            MandatoryUpdateDecision rolledBack = MandatoryUpdatePolicy.ObserveOffline(state,
                firstSeen.AddDays(1));

            Assert.True(expired.MustUpdate);
            Assert.True(rolledBack.MustUpdate);
            Assert.Equal(TimeSpan.Zero, rolledBack.Remaining);
        }

        [Fact]
        public void MandatoryUpdatePolicyClearsWhenCurrentVersionCatchesUp()
        {
            var state = new MandatoryUpdateState();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            MandatoryUpdatePolicy.RecordSuccessfulCheck(state, ReleaseVersion.Parse("8.2.0"),
                ReleaseVersion.Parse("8.3.0-beta.1"), now);

            MandatoryUpdateDecision decision = MandatoryUpdatePolicy.RecordSuccessfulCheck(state,
                ReleaseVersion.Parse("8.3.0"), ReleaseVersion.Parse("8.3.0"), now.AddHours(1));

            Assert.False(decision.UpdatePending);
            Assert.Null(state.PendingVersion);
            Assert.Null(state.FirstSeenUtc);
        }

        [Fact]
        public void MandatoryUpdateStateStoreRoundTripsAtomically()
        {
            using var directory = new TemporaryDirectory();
            var store = new MandatoryUpdateStateStore(
                Path.Combine(directory.Path, "state", "update-state.dat"), new PassthroughProtector());
            var state = new MandatoryUpdateState();
            DateTimeOffset now = new DateTimeOffset(2026, 7, 23, 1, 0, 0, TimeSpan.Zero);
            MandatoryUpdatePolicy.RecordSuccessfulCheck(state, ReleaseVersion.Parse("8.2.0"),
                ReleaseVersion.Parse("8.3.0-beta.1"), now);

            store.Save(state);
            MandatoryUpdateState restored = store.Load();

            Assert.Equal(state.PendingVersion, restored.PendingVersion);
            Assert.Equal(state.FirstSeenUtc, restored.FirstSeenUtc);
            Assert.Equal(state.LastObservedUtc, restored.LastObservedUtc);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(
                Path.Combine(directory.Path, "state", "update-state.dat")), "*.tmp"));
        }

        [Fact]
        public void MandatoryUpdateStateUsesMachineDpapiProtection()
        {
            var protector = new DpapiUpdateStateProtector();
            byte[] value = Encoding.UTF8.GetBytes("TouchPilot update state");

            byte[] protectedValue = protector.Protect(value);
            byte[] restored = protector.Unprotect(protectedValue);

            Assert.NotEqual(value, protectedValue);
            Assert.Equal(value, restored);
        }

        [Fact]
        public async Task ReleasePublisherDeletesReleaseByResolvedId()
        {
            var handler = new ReleaseDeletionHandler();
            using var client = new HttpClient(handler);
            using var publisher = new GitHubReleasePublisher(
                GitHubRepository.Parse("TransposonY/GestureSign"), client);

            GitHubReleaseInfo deleted = await publisher.DeleteAsync("v8.2.0", null,
                CancellationToken.None);

            Assert.Equal(42, deleted.Id);
            Assert.Collection(handler.Requests,
                request =>
                {
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.EndsWith("/releases/tags/v8.2.0", request.Url);
                },
                request =>
                {
                    Assert.Equal(HttpMethod.Delete, request.Method);
                    Assert.EndsWith("/releases/42", request.Url);
                });
        }

        [Fact]
        public void ReleaseManifestUsesCamelCaseAndRoundTrips()
        {
            using var directory = new TemporaryDirectory();
            string path = Path.Combine(directory.Path, ReleaseManifest.FileName);
            var manifest = new ReleaseManifest
            {
                Version = "8.2.0",
                Repository = "TransposonY/GestureSign",
                Runtime = "win-x64",
                Files = new List<ReleaseFileEntry>
                {
                    new ReleaseFileEntry { Path = "GestureSign.exe", Sha256 = "abc", Size = 3 }
                }
            };

            manifest.Save(path);
            string json = File.ReadAllText(path);
            ReleaseManifest restored = ReleaseManifest.Load(path);

            Assert.Contains("\"version\"", json);
            Assert.DoesNotContain("\"Version\"", json);
            Assert.Equal(manifest.Version, restored.Version);
            Assert.Equal(manifest.Repository, restored.Repository);
            Assert.Equal(manifest.Runtime, restored.Runtime);
            Assert.Equal(manifest.Files[0].Path, restored.Files[0].Path);
        }

        [Fact]
        public void UpdaterReplacesManifestFilesAndPreservesUserFiles()
        {
            using var directory = new TemporaryDirectory();
            string targetDirectory = directory.CreateDirectory("target");
            string packagePath = CreatePackage(directory.Path, "8.2.0", new Dictionary<string, string>
            {
                ["GestureSign.exe"] = "new executable",
                ["Languages/en.xml"] = "new language"
            });
            File.WriteAllText(Path.Combine(targetDirectory, "GestureSign.exe"), "old executable");
            File.WriteAllText(Path.Combine(targetDirectory, "user.settings"), "keep me");

            new UpdateInstaller(directory.CreateDirectory("backups"))
                .Install(packagePath, targetDirectory, "8.2.0", ComputeSha256(packagePath));

            Assert.Equal("new executable", File.ReadAllText(Path.Combine(targetDirectory, "GestureSign.exe")));
            Assert.Equal("new language", File.ReadAllText(Path.Combine(targetDirectory, "Languages", "en.xml")));
            Assert.Equal("keep me", File.ReadAllText(Path.Combine(targetDirectory, "user.settings")));
            Assert.True(File.Exists(Path.Combine(targetDirectory, ReleaseManifest.FileName)));
        }

        [Fact]
        public void UpdaterRejectsInvalidHashBeforeChangingTarget()
        {
            using var directory = new TemporaryDirectory();
            string targetDirectory = directory.CreateDirectory("target");
            string packagePath = CreatePackage(directory.Path, "8.2.0", new Dictionary<string, string>
            {
                ["GestureSign.exe"] = "new executable"
            }, corruptFirstHash: true);
            string targetPath = Path.Combine(targetDirectory, "GestureSign.exe");
            File.WriteAllText(targetPath, "old executable");

            Assert.Throws<InvalidDataException>(() =>
                new UpdateInstaller(directory.CreateDirectory("backups"))
                    .Install(packagePath, targetDirectory, "8.2.0", ComputeSha256(packagePath)));

            Assert.Equal("old executable", File.ReadAllText(targetPath));
            Assert.False(File.Exists(Path.Combine(targetDirectory, ReleaseManifest.FileName)));
        }

        [Fact]
        public void UpdaterRejectsChangedPackageBeforeExtraction()
        {
            using var directory = new TemporaryDirectory();
            string targetDirectory = directory.CreateDirectory("target");
            string packagePath = CreatePackage(directory.Path, "8.2.0", new Dictionary<string, string>
            {
                ["GestureSign.exe"] = "new executable"
            });
            string targetPath = Path.Combine(targetDirectory, "GestureSign.exe");
            File.WriteAllText(targetPath, "old executable");

            Assert.Throws<InvalidDataException>(() =>
                new UpdateInstaller(directory.CreateDirectory("backups"))
                    .Install(packagePath, targetDirectory, "8.2.0", new string('0', 64)));

            Assert.Equal("old executable", File.ReadAllText(targetPath));
            Assert.False(File.Exists(Path.Combine(targetDirectory, ReleaseManifest.FileName)));
        }

        private static string CreatePackage(string rootDirectory, string version,
            IReadOnlyDictionary<string, string> files, bool corruptFirstHash = false)
        {
            string sourceDirectory = Path.Combine(rootDirectory, "package-source-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sourceDirectory);
            var entries = new List<ReleaseFileEntry>();
            bool first = true;
            foreach (KeyValuePair<string, string> file in files)
            {
                string path = Path.Combine(sourceDirectory,
                    file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, file.Value);
                entries.Add(new ReleaseFileEntry
                {
                    Path = file.Key,
                    Sha256 = corruptFirstHash && first ? new string('0', 64) : ComputeSha256(path),
                    Size = new FileInfo(path).Length
                });
                first = false;
            }

            new ReleaseManifest
            {
                Version = version,
                Repository = "TransposonY/GestureSign",
                Runtime = "win-x64",
                Files = entries
            }.Save(Path.Combine(sourceDirectory, ReleaseManifest.FileName));

            string packagePath = Path.Combine(rootDirectory, "GestureSign-" + version + ".zip");
            ZipFile.CreateFromDirectory(sourceDirectory, packagePath);
            return packagePath;
        }

        private static UpdateMetadata CreateUpdateMetadata(DateTimeOffset builtAt,
            string version = "8.3.0-beta.1")
        {
            return new UpdateMetadata
            {
                Repository = "Autumn-one/TouchPilot",
                Version = version,
                Tag = "v" + version,
                BuiltAtUtc = builtAt.ToUniversalTime(),
                ExpiresAtUtc = builtAt.ToUniversalTime().AddMonths(3),
                ReleaseNotes = "Beta test",
                Assets = new List<UpdateAssetMetadata>
                {
                    new UpdateAssetMetadata
                    {
                        Distribution = UpdatePackageNaming.InstallerDistribution,
                        Runtime = UpdatePackageNaming.WindowsX64Runtime,
                        Name = UpdatePackageNaming.GetInstallerAssetName(version),
                        Size = 123,
                        Sha256 = new string('a', 64)
                    },
                    new UpdateAssetMetadata
                    {
                        Distribution = UpdatePackageNaming.PortableDistribution,
                        Runtime = UpdatePackageNaming.WindowsX64Runtime,
                        Name = UpdatePackageNaming.GetPortableAssetName(version),
                        Size = 456,
                        Sha256 = new string('b', 64)
                    }
                }
            };
        }

        private sealed class MetadataHandler : HttpMessageHandler
        {
            private readonly IReadOnlyDictionary<string, string> _responses;

            public MetadataHandler(IReadOnlyDictionary<string, string> responses)
            {
                _responses = responses;
            }

            public List<(string Url, bool NoCache, string Authorization)> Requests { get; } =
                new List<(string Url, bool NoCache, string Authorization)>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests.Add((request.RequestUri.ToString(), request.Headers.CacheControl?.NoCache == true,
                    request.Headers.Authorization?.ToString()));
                if (_responses.TryGetValue(request.RequestUri.Host, out string json))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private sealed class PassthroughProtector : IUpdateStateProtector
        {
            public byte[] Protect(byte[] value)
            {
                return value;
            }

            public byte[] Unprotect(byte[] value)
            {
                return value;
            }
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "GestureSign.Tests." + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public string CreateDirectory(string name)
            {
                string path = System.IO.Path.Combine(Path, name);
                Directory.CreateDirectory(path);
                return path;
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

        private sealed class ReleaseDeletionHandler : HttpMessageHandler
        {
            public List<(HttpMethod Method, string Url)> Requests { get; } =
                new List<(HttpMethod Method, string Url)>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests.Add((request.Method, request.RequestUri.ToString()));
                if (request.Method == HttpMethod.Get)
                {
                    const string releaseJson =
                        "{\"id\":42,\"tag_name\":\"v8.2.0\",\"name\":\"GestureSign 8.2.0\"," +
                        "\"html_url\":\"https://github.com/TransposonY/GestureSign/releases/tag/v8.2.0\"," +
                        "\"assets\":[]}";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        }
    }
}

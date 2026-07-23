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

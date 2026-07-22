using GestureSign.Common.Updates;
using GestureSign.Updater;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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
        public void ReleaseVersionNormalizesTagsAndComparesNumerically()
        {
            Version current = ReleaseVersion.Parse("8.1.9");
            Version release = ReleaseVersion.Parse("v8.2.0-beta.1+build.7");

            Assert.True(release > current);
            Assert.Equal("8.2.0", ReleaseVersion.ToReleaseString(release));
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
    }
}

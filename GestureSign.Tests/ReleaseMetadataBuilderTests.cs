using GestureSign.Common.Updates;
using GestureSign.ReleaseManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;

namespace GestureSign.Tests
{
    public class ReleaseMetadataBuilderTests
    {
        [Fact]
        public void MetadataBuilderSignsBothAssetsFromPortableManifestIdentity()
        {
            using var directory = new MetadataTemporaryDirectory();
            const string version = "8.3.0-beta.2";
            DateTimeOffset builtAt = new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero);
            CreateReleaseAssets(directory.Path, version, "Autumn-one/TouchPilot", builtAt);
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            ReleaseMetadataBuildResult result = new ReleaseMetadataBuilder().Build(directory.Path,
                version, "Autumn-one/TouchPilot", "Beta notes", key);
            string json = File.ReadAllText(result.MetadataPath);
            UpdateMetadata verified = UpdateMetadataSignature.Verify(json, key);

            Assert.Equal(UpdatePackageNaming.MetadataAssetName, Path.GetFileName(result.MetadataPath));
            Assert.Equal(builtAt, verified.BuiltAtUtc);
            Assert.Equal(builtAt.AddMonths(3), verified.ExpiresAtUtc);
            Assert.Equal("Beta notes", verified.ReleaseNotes);
            Assert.Equal(UpdateMetadataSignature.GetKeyId(key), result.KeyId);
            Assert.Collection(verified.Assets,
                asset =>
                {
                    Assert.Equal(UpdatePackageNaming.InstallerDistribution, asset.Distribution);
                    Assert.Equal(UpdatePackageNaming.GetInstallerAssetName(version), asset.Name);
                    Assert.True(asset.Size > 0);
                    Assert.Equal(64, asset.Sha256.Length);
                },
                asset =>
                {
                    Assert.Equal(UpdatePackageNaming.PortableDistribution, asset.Distribution);
                    Assert.Equal(UpdatePackageNaming.GetPortableAssetName(version), asset.Name);
                    Assert.True(asset.Size > 0);
                    Assert.Equal(64, asset.Sha256.Length);
                });
        }

        [Fact]
        public void MetadataBuilderRejectsManifestForAnotherRepository()
        {
            using var directory = new MetadataTemporaryDirectory();
            const string version = "8.3.0-beta.2";
            CreateReleaseAssets(directory.Path, version, "another-owner/TouchPilot",
                DateTimeOffset.UtcNow);
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            Assert.Throws<InvalidDataException>(() => new ReleaseMetadataBuilder().Build(
                directory.Path, version, "Autumn-one/TouchPilot", string.Empty, key));
            Assert.False(File.Exists(Path.Combine(directory.Path,
                UpdatePackageNaming.MetadataAssetName)));
        }

        [Fact]
        public void MetadataBuilderRejectsNotesAboveClientLimit()
        {
            using var directory = new MetadataTemporaryDirectory();
            const string version = "8.3.0-beta.2";
            CreateReleaseAssets(directory.Path, version, "Autumn-one/TouchPilot",
                DateTimeOffset.UtcNow);
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            Assert.Throws<InvalidDataException>(() => new ReleaseMetadataBuilder().Build(
                directory.Path, version, "Autumn-one/TouchPilot", new string('x', 70 * 1024), key));
            Assert.False(File.Exists(Path.Combine(directory.Path,
                UpdatePackageNaming.MetadataAssetName)));
        }

        private static void CreateReleaseAssets(string directory, string version, string repository,
            DateTimeOffset builtAt)
        {
            File.WriteAllText(Path.Combine(directory,
                UpdatePackageNaming.GetInstallerAssetName(version)), "installer payload");

            string packageRoot = Path.Combine(directory, "package-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(packageRoot);
            string executablePath = Path.Combine(packageRoot, "TouchPilot.exe");
            File.WriteAllText(executablePath, "portable payload");
            new ReleaseManifest
            {
                Version = version,
                Repository = repository,
                Runtime = UpdatePackageNaming.WindowsX64Runtime,
                Distribution = UpdatePackageNaming.PortableDistribution,
                BuiltAtUtc = builtAt.ToUniversalTime(),
                ExpiresAtUtc = builtAt.ToUniversalTime().AddMonths(3),
                Files = new List<ReleaseFileEntry>
                {
                    new ReleaseFileEntry
                    {
                        Path = "TouchPilot.exe",
                        Size = new FileInfo(executablePath).Length,
                        Sha256 = new string('a', 64)
                    }
                }
            }.Save(Path.Combine(packageRoot, ReleaseManifest.FileName));
            ZipFile.CreateFromDirectory(packageRoot, Path.Combine(directory,
                UpdatePackageNaming.GetPortableAssetName(version)));
        }

        private sealed class MetadataTemporaryDirectory : IDisposable
        {
            public MetadataTemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "TouchPilot.MetadataTests." + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

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

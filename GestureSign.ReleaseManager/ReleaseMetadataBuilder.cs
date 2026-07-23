using GestureSign.Common.Updates;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NuGet.Versioning;

namespace GestureSign.ReleaseManager
{
    internal sealed class ReleaseMetadataBuilder
    {
        private const int MaximumMetadataBytes = 64 * 1024;
        private const int MaximumReleaseManifestBytes = 1024 * 1024;

        private static readonly JsonSerializerOptions ManifestJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public ReleaseMetadataBuildResult Build(string outputDirectory, string version, string repository,
            string releaseNotes, ECDsa signingKey)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("A release output directory is required.", nameof(outputDirectory));
            if (signingKey == null)
                throw new ArgumentNullException(nameof(signingKey));

            outputDirectory = Path.GetFullPath(outputDirectory);
            NuGetVersion parsedVersion = ReleaseVersion.Parse(version);
            string canonicalVersion = ReleaseVersion.ToReleaseString(parsedVersion);
            if (!string.Equals(version, canonicalVersion, StringComparison.Ordinal))
                throw new InvalidDataException("The release version must use canonical SemVer.");
            GitHubRepository parsedRepository = GitHubRepository.Parse(repository);

            string installerName = UpdatePackageNaming.GetInstallerAssetName(canonicalVersion);
            string portableName = UpdatePackageNaming.GetPortableAssetName(canonicalVersion);
            string installerPath = RequireAsset(outputDirectory, installerName);
            string portablePath = RequireAsset(outputDirectory, portableName);
            ReleaseManifest manifest = ReadPortableManifest(portablePath);
            ValidateManifest(manifest, canonicalVersion, parsedRepository.Slug);

            var metadata = new UpdateMetadata
            {
                Repository = parsedRepository.Slug,
                Version = canonicalVersion,
                Tag = "v" + canonicalVersion,
                BuiltAtUtc = manifest.BuiltAtUtc.Value,
                ExpiresAtUtc = manifest.ExpiresAtUtc.Value,
                ReleaseNotes = releaseNotes ?? string.Empty,
                Assets = new List<UpdateAssetMetadata>
                {
                    CreateAsset(UpdatePackageNaming.InstallerDistribution, installerName, installerPath),
                    CreateAsset(UpdatePackageNaming.PortableDistribution, portableName, portablePath)
                }
            };

            string signedJson = UpdateMetadataSignature.Sign(metadata, signingKey);
            if (Encoding.UTF8.GetByteCount(signedJson) > MaximumMetadataBytes)
                throw new InvalidDataException("The signed update metadata exceeds the 64 KiB client limit.");
            _ = UpdateMetadataSignature.Verify(signedJson, signingKey);

            string metadataPath = Path.Combine(outputDirectory, UpdatePackageNaming.MetadataAssetName);
            WriteAtomically(metadataPath, signedJson);
            return new ReleaseMetadataBuildResult(metadataPath, metadata,
                UpdateMetadataSignature.GetKeyId(signingKey));
        }

        private static string RequireAsset(string outputDirectory, string name)
        {
            string path = Path.Combine(outputDirectory, name);
            if (!File.Exists(path))
                throw new FileNotFoundException("A required release asset was not found.", path);
            if (new FileInfo(path).Length <= 0)
                throw new InvalidDataException("A release asset is empty: " + name + ".");
            return path;
        }

        private static ReleaseManifest ReadPortableManifest(string packagePath)
        {
            using ZipArchive archive = ZipFile.OpenRead(packagePath);
            ZipArchiveEntry[] matches = archive.Entries.Where(entry =>
                    string.Equals(entry.FullName, ReleaseManifest.FileName,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("The portable package must contain one release manifest.");
            if (matches[0].Length <= 0 || matches[0].Length > MaximumReleaseManifestBytes)
                throw new InvalidDataException("The portable release manifest has an invalid size.");

            using Stream stream = matches[0].Open();
            ReleaseManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<ReleaseManifest>(stream, ManifestJsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The portable release manifest is invalid.", exception);
            }
            return manifest ?? throw new InvalidDataException("The portable release manifest is empty.");
        }

        private static void ValidateManifest(ReleaseManifest manifest, string version, string repository)
        {
            if (!string.Equals(manifest.Version, version, StringComparison.Ordinal) ||
                !string.Equals(manifest.Repository, repository, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(manifest.Runtime, UpdatePackageNaming.WindowsX64Runtime,
                    StringComparison.Ordinal) ||
                !string.Equals(manifest.Distribution, UpdatePackageNaming.PortableDistribution,
                    StringComparison.Ordinal) ||
                manifest.BuiltAtUtc == null || manifest.ExpiresAtUtc == null ||
                manifest.Files == null || manifest.Files.Count == 0 ||
                manifest.BuiltAtUtc.Value.Offset != TimeSpan.Zero ||
                manifest.ExpiresAtUtc.Value.Offset != TimeSpan.Zero ||
                manifest.ExpiresAtUtc.Value != manifest.BuiltAtUtc.Value.AddMonths(3))
                throw new InvalidDataException(
                    "The portable release manifest does not match the requested release.");
        }

        private static UpdateAssetMetadata CreateAsset(string distribution, string name, string path)
        {
            var file = new FileInfo(path);
            return new UpdateAssetMetadata
            {
                Distribution = distribution,
                Runtime = UpdatePackageNaming.WindowsX64Runtime,
                Name = name,
                Size = file.Length,
                Sha256 = ComputeSha256(path)
            };
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }

        private static void WriteAtomically(string path, string value)
        {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, value, new UTF8Encoding(false));
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }

    internal sealed class ReleaseMetadataBuildResult
    {
        public ReleaseMetadataBuildResult(string metadataPath, UpdateMetadata metadata, string keyId)
        {
            MetadataPath = metadataPath;
            Metadata = metadata;
            KeyId = keyId;
        }

        public string MetadataPath { get; }
        public UpdateMetadata Metadata { get; }
        public string KeyId { get; }
    }
}

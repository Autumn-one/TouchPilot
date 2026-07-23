using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace GestureSign.Common.Updates
{
    public static class UpdateMetadataSignature
    {
        private static readonly JsonSerializerOptions CanonicalJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions FileJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static string Sign(UpdateMetadata metadata, ECDsa privateKey)
        {
            if (privateKey == null)
                throw new ArgumentNullException(nameof(privateKey));

            Validate(metadata);
            byte[] canonicalPayload = SerializeCanonical(metadata);
            byte[] signature = privateKey.SignData(canonicalPayload, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            var signedMetadata = new SignedUpdateMetadata
            {
                Payload = metadata,
                KeyId = GetKeyId(privateKey),
                Signature = Convert.ToBase64String(signature)
            };
            return JsonSerializer.Serialize(signedMetadata, FileJsonOptions);
        }

        public static UpdateMetadata Verify(string json, ECDsa publicKey)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("The update metadata is empty.");
            if (publicKey == null)
                throw new ArgumentNullException(nameof(publicKey));

            SignedUpdateMetadata signedMetadata;
            try
            {
                signedMetadata = JsonSerializer.Deserialize<SignedUpdateMetadata>(json, FileJsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The update metadata is not valid JSON.", exception);
            }

            if (signedMetadata?.Payload == null || string.IsNullOrWhiteSpace(signedMetadata.KeyId) ||
                string.IsNullOrWhiteSpace(signedMetadata.Signature))
                throw new InvalidDataException("The signed update metadata is incomplete.");

            string expectedKeyId = GetKeyId(publicKey);
            if (!string.Equals(signedMetadata.KeyId, expectedKeyId, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("The update metadata was signed by an unknown key.");

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(signedMetadata.Signature);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("The update metadata signature is not valid Base64.", exception);
            }

            byte[] canonicalPayload = SerializeCanonical(signedMetadata.Payload);
            if (!publicKey.VerifyData(canonicalPayload, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new CryptographicException("The update metadata signature is invalid.");

            Validate(signedMetadata.Payload);
            return signedMetadata.Payload;
        }

        public static string GetKeyId(ECDsa key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            return Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        }

        public static void Validate(UpdateMetadata metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));
            if (metadata.SchemaVersion != UpdateMetadata.CurrentSchemaVersion)
                throw new InvalidDataException("The update metadata schema version is not supported.");
            if (!string.Equals(metadata.Product, "TouchPilot", StringComparison.Ordinal))
                throw new InvalidDataException("The update metadata product is invalid.");

            GitHubRepository repository = GitHubRepository.Parse(metadata.Repository);
            string version = ReleaseVersion.ToReleaseString(ReleaseVersion.Parse(metadata.Version));
            if (!string.Equals(metadata.Version, version, StringComparison.Ordinal))
                throw new InvalidDataException("The update metadata version is not canonical SemVer.");
            if (!string.Equals(metadata.Tag, "v" + version, StringComparison.Ordinal))
                throw new InvalidDataException("The update metadata tag does not match its version.");
            if (metadata.BuiltAtUtc.Offset != TimeSpan.Zero || metadata.ExpiresAtUtc.Offset != TimeSpan.Zero)
                throw new InvalidDataException("Update timestamps must use UTC.");
            if (metadata.ExpiresAtUtc != metadata.BuiltAtUtc.AddMonths(3))
                throw new InvalidDataException("The update expiry must be three calendar months after the build.");
            if (metadata.Assets == null || metadata.Assets.Count != 2)
                throw new InvalidDataException("The update metadata must contain installer and portable assets.");

            var distributions = new HashSet<string>(StringComparer.Ordinal);
            foreach (UpdateAssetMetadata asset in metadata.Assets)
            {
                if (asset == null || !UpdatePackageNaming.IsDistributionSupported(asset.Distribution) ||
                    !distributions.Add(asset.Distribution))
                    throw new InvalidDataException("The update metadata contains invalid distributions.");
                if (!string.Equals(asset.Runtime, UpdatePackageNaming.WindowsX64Runtime,
                        StringComparison.Ordinal))
                    throw new InvalidDataException("Only win-x64 update assets are supported.");

                string expectedName = asset.Distribution == UpdatePackageNaming.InstallerDistribution
                    ? UpdatePackageNaming.GetInstallerAssetName(version)
                    : UpdatePackageNaming.GetPortableAssetName(version);
                if (!string.Equals(asset.Name, expectedName, StringComparison.Ordinal))
                    throw new InvalidDataException("An update asset name does not match its distribution.");
                if (asset.Size <= 0 || !IsSha256(asset.Sha256))
                    throw new InvalidDataException("An update asset size or SHA-256 is invalid.");
            }

            if (!distributions.SetEquals(new[]
                {
                    UpdatePackageNaming.InstallerDistribution,
                    UpdatePackageNaming.PortableDistribution
                }))
                throw new InvalidDataException("The update metadata does not contain both distributions.");

            _ = repository;
        }

        private static byte[] SerializeCanonical(UpdateMetadata metadata)
        {
            return JsonSerializer.SerializeToUtf8Bytes(metadata, CanonicalJsonOptions);
        }

        private static bool IsSha256(string value)
        {
            return value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
        }
    }
}

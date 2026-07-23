using GestureSign.Common.Updates;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GestureSign.ReleaseManager
{
    internal static class ReleaseSigningKeyBootstrapper
    {
        internal const string PublicKeyRelativePath =
            @"GestureSign.Common\Updates\TouchPilot-update-public.pem";

        public static SigningKeyBootstrapResult Initialize(string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException("A source directory is required.", nameof(sourceDirectory));

            string root = Path.GetFullPath(sourceDirectory);
            if (!File.Exists(Path.Combine(root, "GestureSign.sln")))
                throw new InvalidOperationException("The selected directory is not the TouchPilot source root.");

            using ECDsa key = new ReleaseSigningKeyStore(root).LoadOrCreate();
            string publicKeyPath = Path.Combine(root, PublicKeyRelativePath);
            ReleasePublicKeyFile.EnsureMatches(publicKeyPath, key);
            return new SigningKeyBootstrapResult(UpdateMetadataSignature.GetKeyId(key), publicKeyPath);
        }
    }

    internal static class ReleasePublicKeyFile
    {
        public static void EnsureMatches(string path, ECDsa signingKey)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A public key path is required.", nameof(path));
            if (signingKey == null)
                throw new ArgumentNullException(nameof(signingKey));

            path = Path.GetFullPath(path);
            if (File.Exists(path))
            {
                using ECDsa existing = ECDsa.Create();
                existing.ImportFromPem(File.ReadAllText(path));
                if (!string.Equals(UpdateMetadataSignature.GetKeyId(existing),
                        UpdateMetadataSignature.GetKeyId(signingKey), StringComparison.OrdinalIgnoreCase))
                    throw new CryptographicException(
                        "The committed update public key does not match the release signing key.");
                return;
            }

            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, signingKey.ExportSubjectPublicKeyInfoPem(),
                    new UTF8Encoding(false));
                try
                {
                    File.Move(temporaryPath, path, false);
                    temporaryPath = null;
                }
                catch (IOException) when (File.Exists(path))
                {
                    EnsureMatches(path, signingKey);
                }
            }
            finally
            {
                if (temporaryPath != null)
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    internal sealed class SigningKeyBootstrapResult
    {
        public SigningKeyBootstrapResult(string keyId, string publicKeyPath)
        {
            KeyId = keyId;
            PublicKeyPath = publicKeyPath;
        }

        public string KeyId { get; }
        public string PublicKeyPath { get; }
    }
}

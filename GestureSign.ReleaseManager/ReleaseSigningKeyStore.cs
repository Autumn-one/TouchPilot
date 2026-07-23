using GestureSign.Common.Updates;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GestureSign.ReleaseManager
{
    internal sealed class ReleaseSigningKeyStore
    {
        internal const string DefaultFileName = "TouchPilot.ReleaseSigningKey.user";
        private const int CurrentSchemaVersion = 1;
        private const string Algorithm = "ECDSA-P256";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private readonly string _path;
        private readonly IReleaseSigningKeyProtector _protector;

        public ReleaseSigningKeyStore(string sourceDirectory)
            : this(GetDefaultPath(sourceDirectory), new DpapiReleaseSigningKeyProtector())
        {
        }

        internal ReleaseSigningKeyStore(string path, IReleaseSigningKeyProtector protector)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("A signing key path is required.", nameof(path))
                : Path.GetFullPath(path);
            _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        }

        public ECDsa LoadOrCreate()
        {
            if (File.Exists(_path))
                return Load();

            ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            try
            {
                SaveNew(key);
                return key;
            }
            catch (IOException) when (File.Exists(_path))
            {
                key.Dispose();
                return Load();
            }
            catch
            {
                key.Dispose();
                throw;
            }
        }

        private ECDsa Load()
        {
            byte[] protectedBytes = null;
            byte[] privateBytes = null;
            try
            {
                StoredSigningKey stored = JsonSerializer.Deserialize<StoredSigningKey>(
                    File.ReadAllText(_path), JsonOptions);
                if (stored == null || stored.SchemaVersion != CurrentSchemaVersion ||
                    !string.Equals(stored.Algorithm, Algorithm, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(stored.KeyId) ||
                    string.IsNullOrWhiteSpace(stored.ProtectedPrivateKey))
                    throw new InvalidDataException("The release signing key file is invalid.");

                protectedBytes = Convert.FromBase64String(stored.ProtectedPrivateKey);
                privateBytes = _protector.Unprotect(protectedBytes);
                ECDsa key = ECDsa.Create();
                try
                {
                    key.ImportPkcs8PrivateKey(privateBytes, out int bytesRead);
                    ECParameters parameters = key.ExportParameters(false);
                    if (bytesRead != privateBytes.Length || key.KeySize != 256 ||
                        !string.Equals(parameters.Curve.Oid.Value,
                            ECCurve.NamedCurves.nistP256.Oid.Value, StringComparison.Ordinal) ||
                        !string.Equals(UpdateMetadataSignature.GetKeyId(key), stored.KeyId,
                            StringComparison.OrdinalIgnoreCase))
                        throw new CryptographicException("The release signing key does not match its identity.");
                    return key;
                }
                catch
                {
                    key.Dispose();
                    throw;
                }
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is JsonException ||
                                              exception is FormatException ||
                                              exception is CryptographicException)
            {
                throw new InvalidDataException("The release signing key could not be loaded safely.", exception);
            }
            finally
            {
                if (privateBytes != null)
                    CryptographicOperations.ZeroMemory(privateBytes);
                if (protectedBytes != null)
                    CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }

        private void SaveNew(ECDsa key)
        {
            byte[] privateBytes = key.ExportPkcs8PrivateKey();
            byte[] protectedBytes = null;
            string temporaryPath = null;
            try
            {
                protectedBytes = _protector.Protect(privateBytes);
                var stored = new StoredSigningKey
                {
                    SchemaVersion = CurrentSchemaVersion,
                    Algorithm = Algorithm,
                    KeyId = UpdateMetadataSignature.GetKeyId(key),
                    ProtectedPrivateKey = Convert.ToBase64String(protectedBytes)
                };

                string directory = Path.GetDirectoryName(_path);
                Directory.CreateDirectory(directory);
                temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(stored, JsonOptions),
                    new UTF8Encoding(false));
                File.Move(temporaryPath, _path, false);
                temporaryPath = null;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateBytes);
                if (protectedBytes != null)
                    CryptographicOperations.ZeroMemory(protectedBytes);
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

        private static string GetDefaultPath(string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException("A source directory is required.", nameof(sourceDirectory));
            return Path.Combine(Path.GetFullPath(sourceDirectory), DefaultFileName);
        }

        private sealed class StoredSigningKey
        {
            public int SchemaVersion { get; set; }
            public string Algorithm { get; set; }
            public string KeyId { get; set; }
            public string ProtectedPrivateKey { get; set; }
        }
    }

    internal interface IReleaseSigningKeyProtector
    {
        byte[] Protect(byte[] value);
        byte[] Unprotect(byte[] value);
    }

    internal sealed class DpapiReleaseSigningKeyProtector : IReleaseSigningKeyProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TouchPilot.ReleaseSigningKey.v1");

        public byte[] Protect(byte[] value)
        {
            return ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser);
        }

        public byte[] Unprotect(byte[] value)
        {
            return ProtectedData.Unprotect(value, Entropy, DataProtectionScope.CurrentUser);
        }
    }
}

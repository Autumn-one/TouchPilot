using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GestureSign.Common.Updates
{
    public sealed class MandatoryUpdateStateStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly string _path;
        private readonly IUpdateStateProtector _protector;

        public MandatoryUpdateStateStore()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "TouchPilot", "update-state.dat"), new DpapiUpdateStateProtector())
        {
        }

        internal MandatoryUpdateStateStore(string path, IUpdateStateProtector protector)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("A mandatory update state path is required.", nameof(path))
                : Path.GetFullPath(path);
            _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        }

        public MandatoryUpdateState Load()
        {
            if (!File.Exists(_path))
                return new MandatoryUpdateState();

            try
            {
                byte[] protectedBytes = File.ReadAllBytes(_path);
                byte[] jsonBytes = _protector.Unprotect(protectedBytes);
                MandatoryUpdateState state = JsonSerializer.Deserialize<MandatoryUpdateState>(
                    jsonBytes, JsonOptions);
                MandatoryUpdatePolicy.Validate(state);
                return state;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is CryptographicException ||
                                              exception is JsonException ||
                                              exception is InvalidDataException ||
                                              exception is FormatException)
            {
                throw new InvalidDataException("The mandatory update state could not be read safely.", exception);
            }
        }

        public void Save(MandatoryUpdateState state)
        {
            MandatoryUpdatePolicy.Validate(state);
            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
            byte[] protectedBytes = _protector.Protect(jsonBytes);
            string directory = Path.GetDirectoryName(_path);
            Directory.CreateDirectory(directory);

            string temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, protectedBytes);
                File.Move(temporaryPath, _path, true);
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

    internal interface IUpdateStateProtector
    {
        byte[] Protect(byte[] value);

        byte[] Unprotect(byte[] value);
    }

    internal sealed class DpapiUpdateStateProtector : IUpdateStateProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TouchPilot.UpdateState.v1");

        public byte[] Protect(byte[] value)
        {
            return ProtectedData.Protect(value, Entropy, DataProtectionScope.LocalMachine);
        }

        public byte[] Unprotect(byte[] value)
        {
            return ProtectedData.Unprotect(value, Entropy, DataProtectionScope.LocalMachine);
        }
    }
}

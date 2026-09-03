using GestureSign.Common.Updates;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace GestureSign.Common.Telemetry
{
    public static class TelemetryConfigurationSignature
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

        public static string Sign(TelemetryConfiguration configuration, ECDsa privateKey)
        {
            if (privateKey == null)
                throw new ArgumentNullException(nameof(privateKey));

            TelemetryConfigurationValidator.Validate(configuration);
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(configuration, CanonicalJsonOptions);
            byte[] signature = privateKey.SignData(payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return JsonSerializer.Serialize(new SignedTelemetryConfiguration
            {
                Payload = configuration,
                KeyId = UpdateMetadataSignature.GetKeyId(privateKey),
                Signature = Convert.ToBase64String(signature)
            }, FileJsonOptions);
        }

        public static TelemetryConfiguration Verify(string json, ECDsa publicKey)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("The telemetry configuration is empty.");
            if (publicKey == null)
                throw new ArgumentNullException(nameof(publicKey));

            SignedTelemetryConfiguration signed;
            try
            {
                signed = JsonSerializer.Deserialize<SignedTelemetryConfiguration>(json, FileJsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The telemetry configuration is not valid JSON.", exception);
            }

            if (signed?.Payload == null || string.IsNullOrWhiteSpace(signed.KeyId) ||
                string.IsNullOrWhiteSpace(signed.Signature))
                throw new InvalidDataException("The signed telemetry configuration is incomplete.");
            if (!string.Equals(signed.KeyId, UpdateMetadataSignature.GetKeyId(publicKey),
                    StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("The telemetry configuration was signed by an unknown key.");

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(signed.Signature);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("The telemetry configuration signature is invalid Base64.",
                    exception);
            }

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(signed.Payload, CanonicalJsonOptions);
            if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new CryptographicException("The telemetry configuration signature is invalid.");

            TelemetryConfigurationValidator.Validate(signed.Payload);
            return signed.Payload;
        }
    }
}

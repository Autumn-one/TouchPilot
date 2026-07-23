using System;
using System.IO;
using System.Security.Cryptography;

namespace GestureSign.Common.Updates
{
    public static class TrustedUpdateSigningKey
    {
        private const string ResourceName = "TouchPilot.UpdatePublicKey";

        public static ECDsa Load()
        {
            using Stream stream = typeof(TrustedUpdateSigningKey).Assembly
                .GetManifestResourceStream(ResourceName);
            if (stream == null)
                throw new InvalidOperationException("The trusted TouchPilot update key is missing.");

            using var reader = new StreamReader(stream);
            string pem = reader.ReadToEnd();
            ECDsa key = ECDsa.Create();
            try
            {
                key.ImportFromPem(pem);
                ECParameters parameters = key.ExportParameters(false);
                if (key.KeySize != 256 || !string.Equals(parameters.Curve.Oid.Value,
                        ECCurve.NamedCurves.nistP256.Oid.Value, StringComparison.Ordinal))
                    throw new CryptographicException("The trusted TouchPilot update key is invalid.");
                return key;
            }
            catch
            {
                key.Dispose();
                throw;
            }
        }

        public static string GetKeyId()
        {
            using ECDsa key = Load();
            return UpdateMetadataSignature.GetKeyId(key);
        }
    }
}

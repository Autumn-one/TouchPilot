using GestureSign.Common.Updates;
using GestureSign.ReleaseManager;
using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace GestureSign.Tests
{
    public class ReleaseSigningTests
    {
        [Fact]
        public void SigningKeyStorePersistsOneEncryptedP256Identity()
        {
            using var directory = new SigningTemporaryDirectory();
            string path = Path.Combine(directory.Path, "signing-key.user");
            var protector = new XorSigningKeyProtector();
            var store = new ReleaseSigningKeyStore(path, protector);

            string firstId;
            using (ECDsa first = store.LoadOrCreate())
            {
                firstId = UpdateMetadataSignature.GetKeyId(first);
                Assert.Equal(256, first.KeySize);
            }

            string persisted = File.ReadAllText(path);
            Assert.True(protector.ProtectCalled);
            Assert.DoesNotContain("BEGIN PRIVATE KEY", persisted);

            using ECDsa second = store.LoadOrCreate();
            Assert.Equal(firstId, UpdateMetadataSignature.GetKeyId(second));
            Assert.True(protector.UnprotectCalled);
        }

        [Fact]
        public void PublicKeyFileMatchesSigningIdentityAndRejectsReplacement()
        {
            using var directory = new SigningTemporaryDirectory();
            string path = Path.Combine(directory.Path, "public.pem");
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using ECDsa differentKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            ReleasePublicKeyFile.EnsureMatches(path, signingKey);
            ReleasePublicKeyFile.EnsureMatches(path, signingKey);

            using ECDsa restored = ECDsa.Create();
            restored.ImportFromPem(File.ReadAllText(path));
            Assert.Equal(UpdateMetadataSignature.GetKeyId(signingKey),
                UpdateMetadataSignature.GetKeyId(restored));
            Assert.Throws<CryptographicException>(() =>
                ReleasePublicKeyFile.EnsureMatches(path, differentKey));
        }

        [Fact]
        public void TrustedUpdateSigningKeyLoadsOneStableP256Identity()
        {
            using ECDsa first = TrustedUpdateSigningKey.Load();
            using ECDsa second = TrustedUpdateSigningKey.Load();

            Assert.Equal(256, first.KeySize);
            Assert.Equal(UpdateMetadataSignature.GetKeyId(first),
                UpdateMetadataSignature.GetKeyId(second));
            Assert.Equal(UpdateMetadataSignature.GetKeyId(first),
                TrustedUpdateSigningKey.GetKeyId());
        }

        private sealed class XorSigningKeyProtector : IReleaseSigningKeyProtector
        {
            public bool ProtectCalled { get; private set; }
            public bool UnprotectCalled { get; private set; }

            public byte[] Protect(byte[] value)
            {
                ProtectCalled = true;
                return Transform(value);
            }

            public byte[] Unprotect(byte[] value)
            {
                UnprotectCalled = true;
                return Transform(value);
            }

            private static byte[] Transform(byte[] value)
            {
                byte[] result = new byte[value.Length];
                for (int index = 0; index < value.Length; index++)
                    result[index] = (byte)(value[index] ^ 0xa5);
                return result;
            }
        }

        private sealed class SigningTemporaryDirectory : IDisposable
        {
            public SigningTemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "TouchPilot.SigningTests." + Guid.NewGuid().ToString("N"));
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

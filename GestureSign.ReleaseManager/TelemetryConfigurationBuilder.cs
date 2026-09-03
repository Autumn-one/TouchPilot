using GestureSign.Common.Telemetry;
using GestureSign.Common.Updates;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GestureSign.ReleaseManager
{
    internal sealed class TelemetryConfigurationBuilder
    {
        public string Build(string sourceDirectory, string repository, string baseAddress,
            int port, long? requestedRevision = null)
        {
            string root = Path.GetFullPath(sourceDirectory ?? string.Empty);
            if (!File.Exists(Path.Combine(root, "GestureSign.sln")))
                throw new InvalidOperationException("The selected directory is not the TouchPilot source root.");

            GitHubRepository parsedRepository = GitHubRepository.Parse(repository);
            SigningKeyBootstrapResult bootstrap = ReleaseSigningKeyBootstrapper.Initialize(root);
            using ECDsa signingKey = new ReleaseSigningKeyStore(root).LoadOrCreate();
            string outputPath = Path.Combine(root,
                TelemetryConfigurationClient.RepositoryPath.Replace('/', Path.DirectorySeparatorChar));
            long previousRevision = ReadPreviousRevision(outputPath, parsedRepository, signingKey);
            long revision = requestedRevision ?? checked(previousRevision + 1);
            if (revision <= previousRevision)
                throw new InvalidDataException(
                    "The telemetry configuration revision must increase monotonically.");

            var configuration = new TelemetryConfiguration
            {
                Repository = parsedRepository.Slug,
                Revision = revision,
                PublishedAtUtc = DateTimeOffset.UtcNow,
                Endpoints =
                {
                    new TelemetryEndpoint
                    {
                        BaseAddress = baseAddress,
                        Port = port,
                        EventPath = "/v1/events"
                    }
                }
            };
            string signedJson = TelemetryConfigurationSignature.Sign(configuration, signingKey);
            WriteAtomically(outputPath, signedJson);

            using ECDsa publicKey = ECDsa.Create();
            publicKey.ImportFromPem(File.ReadAllText(bootstrap.PublicKeyPath));
            _ = TelemetryConfigurationSignature.Verify(File.ReadAllText(outputPath), publicKey);
            return outputPath;
        }

        private static long ReadPreviousRevision(string path, GitHubRepository repository,
            ECDsa signingKey)
        {
            if (!File.Exists(path))
                return 0;
            TelemetryConfiguration existing = TelemetryConfigurationSignature.Verify(
                File.ReadAllText(path), signingKey);
            if (!string.Equals(existing.Repository, repository.Slug,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The existing telemetry configuration belongs to another repository.");
            return existing.Revision;
        }

        private static void WriteAtomically(string path, string value)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, value, new UTF8Encoding(false));
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}

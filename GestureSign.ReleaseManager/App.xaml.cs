using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;

namespace GestureSign.ReleaseManager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length != 0)
            {
                HandleCommandLine(e.Args);
                return;
            }

            new MainWindow().Show();
        }

        private void HandleCommandLine(string[] args)
        {
            try
            {
                if (args.Length == 2 && string.Equals(args[0], "--initialize-signing-key",
                        StringComparison.Ordinal))
                {
                    SigningKeyBootstrapResult result = ReleaseSigningKeyBootstrapper.Initialize(args[1]);
                    Console.WriteLine("TouchPilot signing key ready. Key ID: " + result.KeyId);
                    Console.WriteLine("Public key: " + result.PublicKeyPath);
                    Shutdown(0);
                    return;
                }

                if ((args.Length == 5 || args.Length == 6) &&
                    string.Equals(args[0], "--generate-update-metadata", StringComparison.Ordinal))
                {
                    GenerateUpdateMetadata(args);
                    Shutdown(0);
                    return;
                }

                if (args.Length == 2 && string.Equals(args[0], "--validate-user-config",
                        StringComparison.Ordinal))
                {
                    ReleaseManagerUserConfiguration configuration =
                        ReleaseManagerUserConfiguration.Load(args[1]);
                    Console.WriteLine("Release Manager configuration is valid for " +
                                      configuration.Repository + ".");
                    Shutdown(0);
                    return;
                }

                if ((args.Length == 5 || args.Length == 6) &&
                    string.Equals(args[0], "--generate-telemetry-config", StringComparison.Ordinal))
                {
                    if (!int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int port))
                        throw new ArgumentException("The telemetry port is invalid.");
                    long? revision = null;
                    if (args.Length == 6)
                    {
                        if (!long.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture,
                                out long parsedRevision))
                            throw new ArgumentException("The telemetry revision is invalid.");
                        revision = parsedRevision;
                    }

                    string outputPath = new TelemetryConfigurationBuilder().Build(args[1],
                        args[2], args[3], port, revision);
                    Console.WriteLine("Telemetry configuration: " + outputPath);
                    Shutdown(0);
                    return;
                }

                WriteCommandLineUsage();
                Shutdown(2);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Release command failed: " + exception.Message);
                Shutdown(1);
            }
        }

        private static void GenerateUpdateMetadata(string[] args)
        {
            string sourceDirectory = Path.GetFullPath(args[1]);
            string outputDirectory = Path.GetFullPath(args[2]);
            string releaseNotes = args.Length == 6 ? File.ReadAllText(args[5]) : string.Empty;

            SigningKeyBootstrapResult bootstrap = ReleaseSigningKeyBootstrapper.Initialize(sourceDirectory);
            using ECDsa signingKey = new ReleaseSigningKeyStore(sourceDirectory).LoadOrCreate();
            ReleaseMetadataBuildResult result = new ReleaseMetadataBuilder().Build(outputDirectory,
                args[3], args[4], releaseNotes, signingKey);
            using ECDsa publicKey = ECDsa.Create();
            publicKey.ImportFromPem(File.ReadAllText(bootstrap.PublicKeyPath));
            _ = GestureSign.Common.Updates.UpdateMetadataSignature.Verify(
                File.ReadAllText(result.MetadataPath), publicKey);
            Console.WriteLine("Update metadata: " + result.MetadataPath);
            Console.WriteLine("Signing key ID: " + bootstrap.KeyId);
        }

        private static void WriteCommandLineUsage()
        {
            Console.Error.WriteLine(
                "Usage: TouchPilot.ReleaseManager.exe --initialize-signing-key <source-directory>");
            Console.Error.WriteLine(
                "   or: TouchPilot.ReleaseManager.exe --generate-update-metadata " +
                "<source-directory> <output-directory> <version> <repository> [release-notes-file]");
            Console.Error.WriteLine(
                "   or: TouchPilot.ReleaseManager.exe --validate-user-config <source-directory>");
            Console.Error.WriteLine(
                "   or: TouchPilot.ReleaseManager.exe --generate-telemetry-config " +
                "<source-directory> <repository> <base-address> <port> [revision]");
        }
    }
}

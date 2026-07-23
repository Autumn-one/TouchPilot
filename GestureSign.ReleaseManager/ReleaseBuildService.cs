using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GestureSign.Common.Updates;

namespace GestureSign.ReleaseManager
{
    internal sealed class ReleaseBuildService
    {
        private readonly IReleaseCommandRunner _commandRunner;
        private readonly IReleaseSigningCoordinator _signingCoordinator;

        public ReleaseBuildService()
            : this(new ProcessReleaseCommandRunner(), new ReleaseSigningCoordinator())
        {
        }

        internal ReleaseBuildService(IReleaseCommandRunner commandRunner,
            IReleaseSigningCoordinator signingCoordinator)
        {
            _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
            _signingCoordinator = signingCoordinator ??
                                  throw new ArgumentNullException(nameof(signingCoordinator));
        }

        public async Task<ReleaseBuildResult> BuildReleaseAsync(string sourceDirectory, string version,
            string repository, string releaseNotes, IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            sourceDirectory = string.IsNullOrWhiteSpace(sourceDirectory)
                ? throw new ArgumentException("A source directory is required.", nameof(sourceDirectory))
                : Path.GetFullPath(sourceDirectory);
            string buildScript = Path.Combine(sourceDirectory, "build-release.ps1");
            string testProject = Path.Combine(sourceDirectory, "GestureSign.Tests",
                "GestureSign.Tests.csproj");
            if (!File.Exists(buildScript))
                throw new FileNotFoundException("build-release.ps1 was not found in the source directory.",
                    buildScript);
            if (!File.Exists(testProject))
                throw new FileNotFoundException("The release test project was not found.", testProject);

            string canonicalVersion = ReleaseVersion.ToReleaseString(ReleaseVersion.Parse(version));
            if (!string.Equals(version, canonicalVersion, StringComparison.Ordinal))
                throw new InvalidDataException("The release version must use canonical SemVer.");
            string repositorySlug = GitHubRepository.Parse(repository).Slug;
            string outputDirectory = Path.Combine(sourceDirectory, "artifacts", "release-manager",
                "packages", canonicalVersion);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Preparing the persistent release signing identity...");
            _signingCoordinator.EnsureIdentity(sourceDirectory);

            progress?.Report("Running the complete release test suite...");
            await _commandRunner.RunAsync(new ReleaseCommand("dotnet", sourceDirectory, new[]
            {
                "test", testProject, "-c", "Release", "--nologo",
                "--logger", "console;verbosity=minimal"
            }), progress, cancellationToken).ConfigureAwait(false);

            progress?.Report("Building installer and portable win-x64 assets...");
            await _commandRunner.RunAsync(new ReleaseCommand(FindPowerShell(), sourceDirectory, new[]
            {
                "-NoLogo", "-NoProfile", "-File", buildScript,
                "-Version", canonicalVersion,
                "-Repository", repositorySlug,
                "-OutputDirectory", outputDirectory
            }), progress, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Generating signed update metadata...");
            ReleaseMetadataBuildResult metadata = _signingCoordinator.BuildMetadata(sourceDirectory,
                outputDirectory, canonicalVersion, repositorySlug, releaseNotes ?? string.Empty);
            string installerPath = RequireOutputAsset(outputDirectory,
                UpdatePackageNaming.GetInstallerAssetName(canonicalVersion));
            string portablePath = RequireOutputAsset(outputDirectory,
                UpdatePackageNaming.GetPortableAssetName(canonicalVersion));
            string metadataPath = RequireOutputAsset(outputDirectory,
                UpdatePackageNaming.MetadataAssetName);
            if (!string.Equals(Path.GetFullPath(metadata.MetadataPath), metadataPath,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The metadata generator returned an unexpected path.");

            return new ReleaseBuildResult(outputDirectory, installerPath, portablePath, metadataPath,
                metadata.Metadata, metadata.KeyId);
        }

        private static string FindPowerShell()
        {
            const string installedPowerShell = @"C:\Program Files\PowerShell\7\pwsh.exe";
            return File.Exists(installedPowerShell) ? installedPowerShell : "pwsh.exe";
        }

        private static string RequireOutputAsset(string outputDirectory, string name)
        {
            string path = Path.GetFullPath(Path.Combine(outputDirectory, name));
            if (!File.Exists(path) || new FileInfo(path).Length <= 0)
                throw new InvalidDataException("The release output is missing " + name + ".");
            return path;
        }
    }

    internal sealed class ReleaseBuildResult
    {
        public ReleaseBuildResult(string outputDirectory, string installerPath, string portablePath,
            string metadataPath, UpdateMetadata metadata, string keyId)
        {
            OutputDirectory = outputDirectory;
            InstallerPath = installerPath;
            PortablePath = portablePath;
            MetadataPath = metadataPath;
            Metadata = metadata;
            KeyId = keyId;
        }

        public string OutputDirectory { get; }
        public string InstallerPath { get; }
        public string PortablePath { get; }
        public string MetadataPath { get; }
        public UpdateMetadata Metadata { get; }
        public string KeyId { get; }
        public IReadOnlyList<string> AssetPaths => new[] { InstallerPath, PortablePath, MetadataPath };
    }

    internal sealed class ReleaseCommand
    {
        public ReleaseCommand(string fileName, string workingDirectory, IReadOnlyList<string> arguments)
        {
            FileName = string.IsNullOrWhiteSpace(fileName)
                ? throw new ArgumentException("A command file name is required.", nameof(fileName))
                : fileName;
            WorkingDirectory = Path.GetFullPath(workingDirectory);
            Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        }

        public string FileName { get; }
        public string WorkingDirectory { get; }
        public IReadOnlyList<string> Arguments { get; }
    }

    internal interface IReleaseCommandRunner
    {
        Task RunAsync(ReleaseCommand command, IProgress<string> progress,
            CancellationToken cancellationToken);
    }

    internal sealed class ProcessReleaseCommandRunner : IReleaseCommandRunner
    {
        public async Task RunAsync(ReleaseCommand command, IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                WorkingDirectory = command.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in command.Arguments)
                startInfo.ArgumentList.Add(argument);
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => ReportLine(progress, args.Data, false);
            process.ErrorDataReceived += (_, args) => ReportLine(progress, args.Data, true);

            if (!process.Start())
                throw new InvalidOperationException("Unable to start release command " + command.FileName + ".");
            using ReleaseProcessJob job = ReleaseProcessJob.TryAssign(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (job == null || !job.TryTerminate())
                    TryKillProcessTree(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    command.FileName + " failed with exit code " + process.ExitCode + ".");
        }

        private static void ReportLine(IProgress<string> progress, string value, bool error)
        {
            if (!string.IsNullOrWhiteSpace(value))
                progress?.Report(error ? "ERROR: " + value : value);
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    internal interface IReleaseSigningCoordinator
    {
        void EnsureIdentity(string sourceDirectory);

        ReleaseMetadataBuildResult BuildMetadata(string sourceDirectory, string outputDirectory,
            string version, string repository, string releaseNotes);
    }

    internal sealed class ReleaseSigningCoordinator : IReleaseSigningCoordinator
    {
        public void EnsureIdentity(string sourceDirectory)
        {
            _ = ReleaseSigningKeyBootstrapper.Initialize(sourceDirectory);
        }

        public ReleaseMetadataBuildResult BuildMetadata(string sourceDirectory, string outputDirectory,
            string version, string repository, string releaseNotes)
        {
            using ECDsa signingKey = new ReleaseSigningKeyStore(sourceDirectory).LoadOrCreate();
            ReleaseMetadataBuildResult result = new ReleaseMetadataBuilder().Build(outputDirectory,
                version, repository, releaseNotes, signingKey);

            string publicKeyPath = Path.Combine(sourceDirectory,
                ReleaseSigningKeyBootstrapper.PublicKeyRelativePath);
            using ECDsa publicKey = ECDsa.Create();
            publicKey.ImportFromPem(File.ReadAllText(publicKeyPath));
            _ = UpdateMetadataSignature.Verify(File.ReadAllText(result.MetadataPath), publicKey);
            return result;
        }
    }
}

using GestureSign.Common.Updates;
using GestureSign.ReleaseManager;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GestureSign.Tests
{
    public class ReleaseBuildServiceTests
    {
        [Fact]
        public async Task ReleaseBuildRunsIdentityTestsAssetsAndMetadataInOrder()
        {
            using var directory = new BuildTemporaryDirectory();
            directory.CreateSourceFiles();
            var events = new List<string>();
            var runner = new RecordingReleaseCommandRunner(events);
            var signing = new RecordingSigningCoordinator(events);
            var service = new ReleaseBuildService(runner, signing);

            ReleaseBuildResult result = await service.BuildReleaseAsync(directory.Path,
                "8.3.0-beta.3", "Autumn-one/TouchPilot", "notes", null,
                CancellationToken.None);

            Assert.Equal(new[] { "identity", "command:dotnet", "command:pwsh", "metadata" }, events);
            Assert.Equal(2, runner.Commands.Count);
            Assert.Equal("test", runner.Commands[0].Arguments[0]);
            Assert.Contains("GestureSign.Tests.csproj", runner.Commands[0].Arguments[1]);
            Assert.Contains(runner.Commands[1].Arguments, argument =>
                argument.EndsWith("build-release.ps1", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("8.3.0-beta.3", runner.Commands[1].Arguments);
            Assert.Equal(3, result.AssetPaths.Count);
            Assert.All(result.AssetPaths, path => Assert.True(File.Exists(path)));
            Assert.Equal("notes", signing.ReleaseNotes);
        }

        [Fact]
        public async Task ReleaseBuildDoesNotSignWhenTestsFail()
        {
            using var directory = new BuildTemporaryDirectory();
            directory.CreateSourceFiles();
            var events = new List<string>();
            var runner = new RecordingReleaseCommandRunner(events) { FailCommandIndex = 0 };
            var signing = new RecordingSigningCoordinator(events);
            var service = new ReleaseBuildService(runner, signing);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildReleaseAsync(
                directory.Path, "8.3.0-beta.3", "Autumn-one/TouchPilot", string.Empty, null,
                CancellationToken.None));

            Assert.False(signing.MetadataBuilt);
            Assert.Equal(new[] { "identity", "command:dotnet" }, events);
        }

        [Fact]
        public async Task ReleaseCommandRunnerCancelsAndTerminatesItsProcess()
        {
            const string installedPowerShell = @"C:\Program Files\PowerShell\7\pwsh.exe";
            string executable = File.Exists(installedPowerShell) ? installedPowerShell : "pwsh.exe";
            var runner = new ProcessReleaseCommandRunner();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var stopwatch = Stopwatch.StartNew();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
                new ReleaseCommand(executable, Environment.CurrentDirectory,
                    new[] { "-NoLogo", "-NoProfile", "-Command", "Start-Sleep -Seconds 30" }),
                null, cancellation.Token));

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                "The canceled release process did not terminate promptly.");
        }

        [Fact]
        public async Task ReleaseBuildDoesNotInitializeIdentityWhenAlreadyCanceled()
        {
            using var directory = new BuildTemporaryDirectory();
            directory.CreateSourceFiles();
            var events = new List<string>();
            var service = new ReleaseBuildService(new RecordingReleaseCommandRunner(events),
                new RecordingSigningCoordinator(events));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BuildReleaseAsync(
                directory.Path, "8.3.0-beta.3", "Autumn-one/TouchPilot", string.Empty, null,
                cancellation.Token));

            Assert.Empty(events);
        }

        private sealed class RecordingReleaseCommandRunner : IReleaseCommandRunner
        {
            private readonly List<string> _events;

            public RecordingReleaseCommandRunner(List<string> events)
            {
                _events = events;
            }

            public List<ReleaseCommand> Commands { get; } = new List<ReleaseCommand>();
            public int FailCommandIndex { get; set; } = -1;

            public Task RunAsync(ReleaseCommand command, IProgress<string> progress,
                CancellationToken cancellationToken)
            {
                int index = Commands.Count;
                Commands.Add(command);
                _events.Add("command:" + (command.FileName == "dotnet" ? "dotnet" : "pwsh"));
                if (index == FailCommandIndex)
                    throw new InvalidOperationException("simulated command failure");
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingSigningCoordinator : IReleaseSigningCoordinator
        {
            private readonly List<string> _events;

            public RecordingSigningCoordinator(List<string> events)
            {
                _events = events;
            }

            public bool MetadataBuilt { get; private set; }
            public string ReleaseNotes { get; private set; }

            public void EnsureIdentity(string sourceDirectory)
            {
                _events.Add("identity");
            }

            public ReleaseMetadataBuildResult BuildMetadata(string sourceDirectory,
                string outputDirectory, string version, string repository, string releaseNotes)
            {
                _events.Add("metadata");
                MetadataBuilt = true;
                ReleaseNotes = releaseNotes;
                Directory.CreateDirectory(outputDirectory);
                string installer = Path.Combine(outputDirectory,
                    UpdatePackageNaming.GetInstallerAssetName(version));
                string portable = Path.Combine(outputDirectory,
                    UpdatePackageNaming.GetPortableAssetName(version));
                string metadata = Path.Combine(outputDirectory, UpdatePackageNaming.MetadataAssetName);
                File.WriteAllText(installer, "installer");
                File.WriteAllText(portable, "portable");
                File.WriteAllText(metadata, "metadata");
                return new ReleaseMetadataBuildResult(metadata, new UpdateMetadata
                {
                    Version = version,
                    Repository = repository
                }, "test-key-id");
            }
        }

        private sealed class BuildTemporaryDirectory : IDisposable
        {
            public BuildTemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "TouchPilot.ReleaseBuildTests." + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void CreateSourceFiles()
            {
                File.WriteAllText(System.IO.Path.Combine(Path, "build-release.ps1"), string.Empty);
                string testDirectory = System.IO.Path.Combine(Path, "GestureSign.Tests");
                Directory.CreateDirectory(testDirectory);
                File.WriteAllText(System.IO.Path.Combine(testDirectory,
                    "GestureSign.Tests.csproj"), string.Empty);
            }

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

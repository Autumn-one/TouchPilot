using GestureSign.Common.Updates;
using GestureSign.Common.Telemetry;
using GestureSign.Daemon.Updates;
using GestureSign.Updater;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GestureSign.Tests
{
    public class UpdateCoordinatorTests
    {
        [Theory]
        [InlineData(UpdatePackageNaming.InstallerDistribution,
            "TouchPilot-8.3.0-win-x64-setup.exe")]
        [InlineData(UpdatePackageNaming.PortableDistribution,
            "TouchPilot-8.3.0-win-x64-portable.zip")]
        public async Task NewReleaseDownloadsMatchingDistributionAndStartsUpdater(
            string distribution, string expectedAssetName)
        {
            DateTimeOffset now = new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero);
            var runtime = new FakeUpdateRuntime
            {
                Distribution = distribution,
                UtcNow = now,
                Metadata = CreateMetadata("8.3.0", now)
            };
            using var coordinator = CreateCoordinator(runtime);

            UpdateCycleResult result = await coordinator.CheckNowAsync(CancellationToken.None);

            Assert.Equal(UpdateCycleResult.UpdateStarted, result);
            Assert.Equal(expectedAssetName, runtime.DownloadedAsset.Name);
            Assert.Same(runtime.DownloadedAsset, runtime.StartedAsset);
            Assert.Equal("downloaded-package", runtime.StartedPackagePath);
            Assert.Equal("8.3.0", runtime.State.PendingVersion);
            Assert.Equal(now, runtime.State.FirstSeenUtc);
            Assert.Equal(1, runtime.ExitRequests);
            Assert.Contains(runtime.ProgressUpdates,
                update => update.Stage == UpdateProgressStage.Downloading && update.Percentage == 42);
            Assert.Contains(runtime.ProgressUpdates,
                update => update.Stage == UpdateProgressStage.PreparingInstallation);
        }

        [Fact]
        public async Task FirstOfflineCheckKeepsApplicationRunning()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero);
            var runtime = new FakeUpdateRuntime
            {
                UtcNow = now,
                MetadataException = new UpdateMetadataUnavailableException(new[] { "offline" })
            };
            using var coordinator = CreateCoordinator(runtime);

            UpdateCycleResult result = await coordinator.CheckNowAsync(CancellationToken.None);

            Assert.Equal(UpdateCycleResult.OfflineAllowed, result);
            Assert.Equal(now, runtime.State.LastObservedUtc);
            Assert.Equal(0, runtime.ExitRequests);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task KnownUpdateWaitsForRetryWhenOffline(int offlineDays)
        {
            DateTimeOffset firstSeen = new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.Zero);
            var state = new MandatoryUpdateState();
            MandatoryUpdatePolicy.RecordSuccessfulCheck(state, ReleaseVersion.Parse("8.2.0"),
                ReleaseVersion.Parse("8.3.0"), firstSeen);
            var runtime = new FakeUpdateRuntime
            {
                State = state,
                UtcNow = firstSeen.AddDays(offlineDays),
                MetadataException = new UpdateMetadataUnavailableException(new[] { "offline" })
            };
            using var coordinator = CreateCoordinator(runtime);

            UpdateCycleResult result = await coordinator.CheckNowAsync(CancellationToken.None);

            Assert.Equal(UpdateCycleResult.OfflineAllowed, result);
            Assert.Equal(0, runtime.ExitRequests);
            Assert.Equal(firstSeen, runtime.State.FirstSeenUtc);
            Assert.Contains(runtime.ProgressUpdates,
                update => update.Stage == UpdateProgressStage.RetryPending &&
                          update.Version == "8.3.0");
        }

        [Fact]
        public async Task CurrentReleaseClearsPreviouslyPendingUpdate()
        {
            DateTimeOffset firstSeen = new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.Zero);
            var state = new MandatoryUpdateState();
            MandatoryUpdatePolicy.RecordSuccessfulCheck(state, ReleaseVersion.Parse("8.2.0"),
                ReleaseVersion.Parse("8.3.0"), firstSeen);
            var runtime = new FakeUpdateRuntime
            {
                CurrentVersion = ReleaseVersion.Parse("8.3.0"),
                State = state,
                UtcNow = firstSeen.AddHours(1),
                Metadata = CreateMetadata("8.3.0", firstSeen)
            };
            using var coordinator = CreateCoordinator(runtime);

            UpdateCycleResult result = await coordinator.CheckNowAsync(CancellationToken.None);

            Assert.Equal(UpdateCycleResult.Current, result);
            Assert.Null(runtime.State.PendingVersion);
            Assert.Null(runtime.State.FirstSeenUtc);
            Assert.Equal(0, runtime.ExitRequests);
            Assert.Equal(1, runtime.CloseWindowRequests);
        }

        [Fact]
        public async Task StaleMirrorDoesNotClearOrDowngradeKnownUpdate()
        {
            DateTimeOffset firstSeen = new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.Zero);
            var state = new MandatoryUpdateState();
            MandatoryUpdatePolicy.RecordSuccessfulCheck(state, ReleaseVersion.Parse("8.2.0"),
                ReleaseVersion.Parse("8.4.0"), firstSeen);
            var runtime = new FakeUpdateRuntime
            {
                State = state,
                UtcNow = firstSeen.AddHours(1),
                Metadata = CreateMetadata("8.3.0", firstSeen)
            };
            using var coordinator = CreateCoordinator(runtime);

            UpdateCycleResult result = await coordinator.CheckNowAsync(CancellationToken.None);

            Assert.Equal(UpdateCycleResult.OfflineAllowed, result);
            Assert.Equal("8.4.0", runtime.State.PendingVersion);
            Assert.Equal("8.4.0", runtime.RequestedMinimumVersion.ToNormalizedString());
            Assert.Null(runtime.DownloadedAsset);
            Assert.Contains(runtime.ProgressUpdates,
                update => update.Stage == UpdateProgressStage.RetryPending &&
                          update.Version == "8.4.0");
        }

        [Fact]
        public async Task KnownUpdateWaitsForRetryWhenPackageDownloadFails()
        {
            DateTimeOffset firstSeen = new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.Zero);
            var state = new MandatoryUpdateState();
            MandatoryUpdatePolicy.RecordSuccessfulCheck(state, ReleaseVersion.Parse("8.2.0"),
                ReleaseVersion.Parse("8.3.0"), firstSeen);
            var runtime = new FakeUpdateRuntime
            {
                State = state,
                UtcNow = firstSeen.AddDays(3),
                Metadata = CreateMetadata("8.3.0", firstSeen),
                DownloadException = new IOException("download failed")
            };
            using var coordinator = CreateCoordinator(runtime);

            UpdateCycleResult result = await coordinator.CheckNowAsync(CancellationToken.None);

            Assert.Equal(UpdateCycleResult.OfflineAllowed, result);
            Assert.Equal(0, runtime.ExitRequests);
            Assert.Same(runtime.DownloadException, runtime.LoggedException);
            Assert.Equal(UpdateProgressStage.RetryPending,
                Assert.Single(runtime.ProgressUpdates,
                    update => update.Stage == UpdateProgressStage.RetryPending).Stage);
        }

        [Fact]
        public async Task ScheduledChecksContinueAtConfiguredInterval()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero);
            var runtime = new FakeUpdateRuntime
            {
                CurrentVersion = ReleaseVersion.Parse("8.3.0"),
                UtcNow = now,
                Metadata = CreateMetadata("8.3.0", now)
            };
            using var coordinator = new UpdateCoordinator(runtime, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(10));

            coordinator.ScheduleStartupCheck();
            coordinator.ScheduleStartupCheck();
            await runtime.TwoChecks.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(Volatile.Read(ref runtime.CheckCount) >= 2);
        }

        [Fact]
        public void ProductionScheduleChecksImmediatelyAndEveryTenMinutes()
        {
            Assert.Equal(TimeSpan.Zero, UpdateCoordinator.StartupDelay);
            Assert.Equal(TimeSpan.FromMinutes(10), UpdateCoordinator.CheckInterval);
        }

        [Fact]
        public void UpdateWindowsCenterWithinPrimaryWorkingArea()
        {
            var workingArea = new Rectangle(100, 50, 1920, 1040);
            var size = new Size(480, 226);

            Assert.Equal(new Point(820, 457),
                UpdateProgressForm.GetCenteredLocation(workingArea, size));
            Assert.Equal(new Point(820, 457),
                InstallationProgressForm.GetCenteredLocation(workingArea, size));
        }

        [Fact]
        public async Task DecliningUpdaterElevationWaitsForRetry()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero);
            var runtime = new FakeUpdateRuntime
            {
                UtcNow = now,
                Metadata = CreateMetadata("8.3.0", now),
                UpdaterStarted = false
            };
            using var coordinator = CreateCoordinator(runtime);

            UpdateCycleResult result = await coordinator.CheckNowAsync(CancellationToken.None);

            Assert.Equal(UpdateCycleResult.UpdateLaunchDeclined, result);
            Assert.Equal(0, runtime.ExitRequests);
            Assert.Contains(runtime.ProgressUpdates,
                update => update.Stage == UpdateProgressStage.RetryPending);
        }

        [Fact]
        public async Task UnreadableMandatoryStateIsRecovered()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero);
            var runtime = new FakeUpdateRuntime
            {
                LoadException = new InvalidDataException("corrupt state"),
                UtcNow = now,
                Metadata = CreateMetadata("8.2.0", now)
            };
            using var coordinator = CreateCoordinator(runtime);

            UpdateCycleResult result = await coordinator.CheckNowAsync(CancellationToken.None);

            Assert.Equal(UpdateCycleResult.Current, result);
            Assert.Equal(0, runtime.ExitRequests);
            Assert.IsType<InvalidDataException>(runtime.LoggedException);
        }

        [Fact]
        public async Task ManualCheckReportsCurrentRelease()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero);
            var runtime = new FakeUpdateRuntime
            {
                CurrentVersion = ReleaseVersion.Parse("8.3.0"),
                UtcNow = now,
                Metadata = CreateMetadata("8.3.0", now)
            };
            using var coordinator = CreateCoordinator(runtime);

            UpdateCycleResult result = await coordinator.CheckNowAsync(CancellationToken.None, true);

            Assert.Equal(UpdateCycleResult.Current, result);
            Assert.Equal(ManualUpdateCheckResult.Current, runtime.ManualCheckResult);
        }

        [Fact]
        public async Task RepeatedFailureTracksAvailableVersionOnce()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero);
            var runtime = new FakeUpdateRuntime
            {
                UtcNow = now,
                Metadata = CreateMetadata("8.3.0", now),
                UpdaterStarted = false
            };
            var telemetry = new RecordingTelemetrySink();
            using var coordinator = new UpdateCoordinator(runtime, TimeSpan.Zero,
                TimeSpan.FromHours(1), telemetry);

            await coordinator.CheckNowAsync(CancellationToken.None);
            await coordinator.CheckNowAsync(CancellationToken.None);

            Assert.Single(telemetry.Events,
                telemetryEvent => telemetryEvent == "update_available");
        }

        private static UpdateCoordinator CreateCoordinator(FakeUpdateRuntime runtime)
        {
            return new UpdateCoordinator(runtime, TimeSpan.Zero, TimeSpan.FromHours(1));
        }

        private static UpdateMetadata CreateMetadata(string version, DateTimeOffset builtAt)
        {
            return new UpdateMetadata
            {
                Repository = "Autumn-one/TouchPilot",
                Version = version,
                Tag = "v" + version,
                BuiltAtUtc = builtAt,
                ExpiresAtUtc = builtAt.AddMonths(3),
                Assets = new List<UpdateAssetMetadata>
                {
                    new UpdateAssetMetadata
                    {
                        Distribution = UpdatePackageNaming.InstallerDistribution,
                        Runtime = UpdatePackageNaming.WindowsX64Runtime,
                        Name = UpdatePackageNaming.GetInstallerAssetName(version),
                        Size = 100,
                        Sha256 = new string('a', 64)
                    },
                    new UpdateAssetMetadata
                    {
                        Distribution = UpdatePackageNaming.PortableDistribution,
                        Runtime = UpdatePackageNaming.WindowsX64Runtime,
                        Name = UpdatePackageNaming.GetPortableAssetName(version),
                        Size = 100,
                        Sha256 = new string('b', 64)
                    }
                }
            };
        }

        private sealed class FakeUpdateRuntime : IUpdateCoordinatorRuntime
        {
            public bool IsSelfUpdateSupported { get; set; } = true;

            public NuGetVersion CurrentVersion { get; set; } = ReleaseVersion.Parse("8.2.0");

            public string Distribution { get; set; } = UpdatePackageNaming.InstallerDistribution;

            public string RuntimeIdentifier { get; set; } = UpdatePackageNaming.WindowsX64Runtime;

            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

            public MandatoryUpdateState State { get; set; } = new MandatoryUpdateState();

            public UpdateMetadata Metadata { get; set; }

            public Exception MetadataException { get; set; }

            public Exception LoadException { get; set; }

            public Exception DownloadException { get; set; }

            public bool UpdaterStarted { get; set; } = true;

            public UpdateAssetMetadata DownloadedAsset { get; private set; }

            public UpdateAssetMetadata StartedAsset { get; private set; }

            public string StartedPackagePath { get; private set; }

            public int ExitRequests { get; private set; }

            public Exception LoggedException { get; private set; }

            public List<(UpdateProgressStage Stage, string Version, double Percentage)>
                ProgressUpdates { get; } = new List<(UpdateProgressStage, string, double)>();

            public int CloseWindowRequests { get; private set; }

            public ManualUpdateCheckResult? ManualCheckResult { get; private set; }

            public int CheckCount;
            public NuGetVersion RequestedMinimumVersion { get; private set; }

            public TaskCompletionSource<bool> TwoChecks { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public MandatoryUpdateState LoadState()
            {
                if (LoadException != null)
                    throw LoadException;
                return State;
            }

            public void SaveState(MandatoryUpdateState state)
            {
                State = state;
            }

            public Task<UpdateMetadata> GetLatestMetadataAsync(NuGetVersion minimumVersion,
                CancellationToken cancellationToken)
            {
                RequestedMinimumVersion = minimumVersion;
                if (Interlocked.Increment(ref CheckCount) >= 2)
                    TwoChecks.TrySetResult(true);
                if (MetadataException != null)
                    return Task.FromException<UpdateMetadata>(MetadataException);
                return Task.FromResult(Metadata);
            }

            public Task<string> DownloadUpdateAsync(UpdateMetadata metadata, UpdateAssetMetadata asset,
                IProgress<double> progress, CancellationToken cancellationToken)
            {
                DownloadedAsset = asset;
                if (DownloadException != null)
                    return Task.FromException<string>(DownloadException);
                progress?.Report(42);
                return Task.FromResult("downloaded-package");
            }

            public Task<bool> StartUpdaterAsync(UpdateMetadata metadata, UpdateAssetMetadata asset,
                string packagePath, CancellationToken cancellationToken)
            {
                StartedAsset = asset;
                StartedPackagePath = packagePath;
                return Task.FromResult(UpdaterStarted);
            }

            public void ExitApplication()
            {
                ExitRequests++;
            }

            public void LogException(Exception exception)
            {
                LoggedException = exception;
            }

            public void ShowUpdateProgress(UpdateProgressStage stage, string version, double percentage)
            {
                ProgressUpdates.Add((stage, version, percentage));
            }

            public void CloseUpdateWindow()
            {
                CloseWindowRequests++;
            }

            public void ShowManualCheckResult(ManualUpdateCheckResult result)
            {
                ManualCheckResult = result;
            }
        }

        private sealed class RecordingTelemetrySink : ITelemetrySink
        {
            public List<string> Events { get; } = new List<string>();

            public void Track(string name, IReadOnlyDictionary<string, string> properties = null)
            {
                Events.Add(name);
            }
        }
    }
}

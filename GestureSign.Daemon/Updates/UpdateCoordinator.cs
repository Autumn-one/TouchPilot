using GestureSign.Common.Updates;
using GestureSign.Common.Telemetry;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.Daemon.Updates
{
    internal sealed class UpdateCoordinator : IDisposable
    {
        internal static readonly TimeSpan StartupDelay = TimeSpan.Zero;
        internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);

        private readonly IUpdateCoordinatorRuntime _runtime;
        private readonly TimeSpan _startupDelay;
        private readonly TimeSpan _checkInterval;
        private readonly ITelemetrySink _telemetry;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private int _scheduled;
        private int _checking;
        private int _disposed;
        private string _reportedAvailableVersion;

        public UpdateCoordinator(SynchronizationContext uiContext, ITelemetrySink telemetry = null)
            : this(new SystemUpdateCoordinatorRuntime(uiContext), StartupDelay, CheckInterval, telemetry)
        {
        }

        internal UpdateCoordinator(IUpdateCoordinatorRuntime runtime, TimeSpan startupDelay,
            TimeSpan checkInterval, ITelemetrySink telemetry = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _startupDelay = startupDelay >= TimeSpan.Zero
                ? startupDelay
                : throw new ArgumentOutOfRangeException(nameof(startupDelay));
            _checkInterval = checkInterval > TimeSpan.Zero
                ? checkInterval
                : throw new ArgumentOutOfRangeException(nameof(checkInterval));
            _telemetry = telemetry ?? NullTelemetrySink.Instance;
        }

        public void ScheduleStartupCheck()
        {
            if (!_runtime.IsSelfUpdateSupported || Interlocked.Exchange(ref _scheduled, 1) != 0)
                return;

            _ = Task.Run(() => RunCheckLoopAsync(_shutdown.Token));
        }

        public void RequestManualCheck()
        {
            _telemetry.Track("manual_update_check_requested");
            _ = CheckNowAsync(_shutdown.Token, true);
        }

        internal async Task<UpdateCycleResult> CheckNowAsync(CancellationToken cancellationToken)
        {
            return await CheckNowAsync(cancellationToken, false).ConfigureAwait(false);
        }

        internal async Task<UpdateCycleResult> CheckNowAsync(CancellationToken cancellationToken,
            bool manual)
        {
            if (!_runtime.IsSelfUpdateSupported)
            {
                if (manual)
                    _runtime.ShowManualCheckResult(ManualUpdateCheckResult.Unsupported);
                return UpdateCycleResult.Unsupported;
            }
            if (Interlocked.Exchange(ref _checking, 1) != 0)
            {
                if (manual)
                    _runtime.ShowManualCheckResult(ManualUpdateCheckResult.AlreadyRunning);
                return UpdateCycleResult.AlreadyRunning;
            }

            try
            {
                MandatoryUpdateState state;
                try
                {
                    state = _runtime.LoadState();
                }
                catch (Exception exception) when (exception is IOException ||
                                                  exception is UnauthorizedAccessException ||
                                                  exception is InvalidDataException)
                {
                    _runtime.LogException(exception);
                    state = new MandatoryUpdateState();
                }

                MandatoryUpdateDecision decision = null;
                try
                {
                    NuGetVersion minimumVersion = _runtime.CurrentVersion;
                    if (ReleaseVersion.TryParse(state.PendingVersion, out NuGetVersion pendingVersion) &&
                        VersionComparer.VersionRelease.Compare(pendingVersion, minimumVersion) > 0)
                        minimumVersion = pendingVersion;
                    UpdateMetadata metadata = await _runtime.GetLatestMetadataAsync(minimumVersion, cancellationToken)
                        .ConfigureAwait(false);
                    NuGetVersion latestVersion = ReleaseVersion.Parse(metadata.Version);
                    decision = MandatoryUpdatePolicy.RecordSuccessfulCheck(state,
                        _runtime.CurrentVersion, latestVersion, _runtime.UtcNow);
                    TrySaveState(state);

                    if (!decision.UpdatePending)
                    {
                        _reportedAvailableVersion = null;
                        _runtime.CloseUpdateWindow();
                        if (manual)
                            _runtime.ShowManualCheckResult(ManualUpdateCheckResult.Current);
                        return UpdateCycleResult.Current;
                    }

                    string latestVersionText = ReleaseVersion.ToReleaseString(latestVersion);
                    if (!string.Equals(decision.PendingVersion, latestVersionText,
                            StringComparison.Ordinal))
                    {
                        _runtime.ShowUpdateProgress(UpdateProgressStage.RetryPending,
                            decision.PendingVersion, 0);
                        return UpdateCycleResult.OfflineAllowed;
                    }

                    UpdateAssetMetadata asset = UpdateInstallation.FindAsset(metadata,
                        _runtime.Distribution, _runtime.RuntimeIdentifier);
                    if (!string.Equals(_reportedAvailableVersion, metadata.Version,
                            StringComparison.Ordinal))
                    {
                        _telemetry.Track("update_available", VersionProperties(metadata.Version));
                        _reportedAvailableVersion = metadata.Version;
                    }
                    _runtime.ShowUpdateProgress(UpdateProgressStage.Downloading, metadata.Version, 0);
                    var progress = new InlineProgress(value =>
                        _runtime.ShowUpdateProgress(UpdateProgressStage.Downloading,
                            metadata.Version, value));
                    string packagePath = await _runtime.DownloadUpdateAsync(metadata, asset,
                        progress, cancellationToken).ConfigureAwait(false);
                    _runtime.ShowUpdateProgress(UpdateProgressStage.PreparingInstallation,
                        metadata.Version, 100);
                    bool started = await _runtime.StartUpdaterAsync(metadata, asset, packagePath,
                        cancellationToken).ConfigureAwait(false);
                    if (!started)
                    {
                        _runtime.ShowUpdateProgress(UpdateProgressStage.RetryPending,
                            metadata.Version, 100);
                        return UpdateCycleResult.UpdateLaunchDeclined;
                    }

                    _telemetry.Track("update_started", VersionProperties(metadata.Version));
                    _runtime.ExitApplication();
                    return UpdateCycleResult.UpdateStarted;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _runtime.LogException(exception);
                    return HandleUnavailableUpdate(state, decision, manual);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _shutdown.Cancel();
        }

        private async Task RunCheckLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(_startupDelay, cancellationToken).ConfigureAwait(false);
                while (true)
                {
                    await CheckNowAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(_checkInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private UpdateCycleResult HandleUnavailableUpdate(MandatoryUpdateState state,
            MandatoryUpdateDecision decision, bool manual)
        {
            try
            {
                decision ??= MandatoryUpdatePolicy.ObserveOffline(state, _runtime.UtcNow);
                TrySaveState(state);
            }
            catch (Exception exception)
            {
                _runtime.LogException(exception);
            }

            if (decision?.UpdatePending == true)
            {
                _runtime.ShowUpdateProgress(UpdateProgressStage.RetryPending,
                    decision.PendingVersion, 0);
            }
            else if (manual)
            {
                _runtime.ShowManualCheckResult(ManualUpdateCheckResult.Unavailable);
            }

            return UpdateCycleResult.OfflineAllowed;
        }

        private void TrySaveState(MandatoryUpdateState state)
        {
            try
            {
                _runtime.SaveState(state);
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is InvalidDataException)
            {
                _runtime.LogException(exception);
            }
        }

        private static IReadOnlyDictionary<string, string> VersionProperties(string version)
        {
            return new Dictionary<string, string> { ["version"] = version ?? string.Empty };
        }

        private sealed class InlineProgress : IProgress<double>
        {
            private readonly Action<double> _report;

            public InlineProgress(Action<double> report)
            {
                _report = report;
            }

            public void Report(double value)
            {
                _report(value);
            }
        }
    }

    internal enum UpdateCycleResult
    {
        Unsupported,
        AlreadyRunning,
        Current,
        OfflineAllowed,
        UpdateStarted,
        UpdateLaunchDeclined,
        MandatoryUpdateBlocked,
        InvalidStateBlocked
    }

    internal enum UpdateProgressStage
    {
        Downloading,
        PreparingInstallation,
        RetryPending
    }

    internal enum ManualUpdateCheckResult
    {
        Current,
        Unavailable,
        Unsupported,
        AlreadyRunning
    }

    internal interface IUpdateCoordinatorRuntime
    {
        bool IsSelfUpdateSupported { get; }

        NuGetVersion CurrentVersion { get; }

        string Distribution { get; }

        string RuntimeIdentifier { get; }

        DateTimeOffset UtcNow { get; }

        MandatoryUpdateState LoadState();

        void SaveState(MandatoryUpdateState state);

        Task<UpdateMetadata> GetLatestMetadataAsync(NuGetVersion minimumVersion, CancellationToken cancellationToken);

        Task<string> DownloadUpdateAsync(UpdateMetadata metadata, UpdateAssetMetadata asset,
            IProgress<double> progress, CancellationToken cancellationToken);

        Task<bool> StartUpdaterAsync(UpdateMetadata metadata, UpdateAssetMetadata asset,
            string packagePath, CancellationToken cancellationToken);

        void ExitApplication();

        void LogException(Exception exception);

        void ShowUpdateProgress(UpdateProgressStage stage, string version, double percentage);

        void CloseUpdateWindow();

        void ShowManualCheckResult(ManualUpdateCheckResult result);
    }
}

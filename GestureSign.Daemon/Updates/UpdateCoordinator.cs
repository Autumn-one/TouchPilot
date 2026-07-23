using GestureSign.Common.Updates;
using NuGet.Versioning;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.Daemon.Updates
{
    internal sealed class UpdateCoordinator : IDisposable
    {
        internal static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
        internal static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

        private readonly IUpdateCoordinatorRuntime _runtime;
        private readonly TimeSpan _startupDelay;
        private readonly TimeSpan _checkInterval;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private int _scheduled;
        private int _checking;
        private int _disposed;

        public UpdateCoordinator(SynchronizationContext uiContext)
            : this(new SystemUpdateCoordinatorRuntime(uiContext), StartupDelay, CheckInterval)
        {
        }

        internal UpdateCoordinator(IUpdateCoordinatorRuntime runtime, TimeSpan startupDelay,
            TimeSpan checkInterval)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _startupDelay = startupDelay >= TimeSpan.Zero
                ? startupDelay
                : throw new ArgumentOutOfRangeException(nameof(startupDelay));
            _checkInterval = checkInterval > TimeSpan.Zero
                ? checkInterval
                : throw new ArgumentOutOfRangeException(nameof(checkInterval));
        }

        public void ScheduleStartupCheck()
        {
            if (!_runtime.IsSelfUpdateSupported || Interlocked.Exchange(ref _scheduled, 1) != 0)
                return;

            _ = Task.Run(() => RunCheckLoopAsync(_shutdown.Token));
        }

        internal async Task<UpdateCycleResult> CheckNowAsync(CancellationToken cancellationToken)
        {
            if (!_runtime.IsSelfUpdateSupported)
                return UpdateCycleResult.Unsupported;
            if (Interlocked.Exchange(ref _checking, 1) != 0)
                return UpdateCycleResult.AlreadyRunning;

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
                    _runtime.ExitApplication();
                    return UpdateCycleResult.InvalidStateBlocked;
                }

                MandatoryUpdateDecision decision = null;
                try
                {
                    UpdateMetadata metadata = await _runtime.GetLatestMetadataAsync(cancellationToken)
                        .ConfigureAwait(false);
                    NuGetVersion latestVersion = ReleaseVersion.Parse(metadata.Version);
                    decision = MandatoryUpdatePolicy.RecordSuccessfulCheck(state,
                        _runtime.CurrentVersion, latestVersion, _runtime.UtcNow);
                    _runtime.SaveState(state);

                    if (!decision.UpdatePending)
                        return UpdateCycleResult.Current;

                    UpdateAssetMetadata asset = UpdateInstallation.FindAsset(metadata,
                        _runtime.Distribution, _runtime.RuntimeIdentifier);
                    string packagePath = await _runtime.DownloadUpdateAsync(metadata, asset,
                        cancellationToken).ConfigureAwait(false);
                    bool started = await _runtime.StartUpdaterAsync(metadata, asset, packagePath,
                        cancellationToken).ConfigureAwait(false);
                    if (!started)
                    {
                        _runtime.ExitApplication();
                        return UpdateCycleResult.UpdateLaunchDeclined;
                    }

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
                    return HandleUnavailableUpdate(state, decision);
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
            MandatoryUpdateDecision decision)
        {
            try
            {
                decision ??= MandatoryUpdatePolicy.ObserveOffline(state, _runtime.UtcNow);
                _runtime.SaveState(state);
            }
            catch (Exception exception)
            {
                _runtime.LogException(exception);
                _runtime.ExitApplication();
                return UpdateCycleResult.InvalidStateBlocked;
            }

            if (!decision.MustUpdate)
                return UpdateCycleResult.OfflineAllowed;

            _runtime.ExitApplication();
            return UpdateCycleResult.MandatoryUpdateBlocked;
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

    internal interface IUpdateCoordinatorRuntime
    {
        bool IsSelfUpdateSupported { get; }

        NuGetVersion CurrentVersion { get; }

        string Distribution { get; }

        string RuntimeIdentifier { get; }

        DateTimeOffset UtcNow { get; }

        MandatoryUpdateState LoadState();

        void SaveState(MandatoryUpdateState state);

        Task<UpdateMetadata> GetLatestMetadataAsync(CancellationToken cancellationToken);

        Task<string> DownloadUpdateAsync(UpdateMetadata metadata, UpdateAssetMetadata asset,
            CancellationToken cancellationToken);

        Task<bool> StartUpdaterAsync(UpdateMetadata metadata, UpdateAssetMetadata asset,
            string packagePath, CancellationToken cancellationToken);

        void ExitApplication();

        void LogException(Exception exception);
    }
}

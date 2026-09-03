using GestureSign.Common;
using GestureSign.Common.Configuration;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.Updates;
using NuGet.Versioning;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GestureSign.Daemon.Updates
{
    internal sealed class SystemUpdateCoordinatorRuntime : IUpdateCoordinatorRuntime
    {
        private readonly SynchronizationContext _uiContext;
        private readonly MandatoryUpdateStateStore _stateStore = new MandatoryUpdateStateStore();
        private UpdateProgressForm _updateProgressForm;

        public SystemUpdateCoordinatorRuntime(SynchronizationContext uiContext)
        {
            _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
        }

        public bool IsSelfUpdateSupported => UpdateInstallation.IsSelfUpdateSupported;

        public NuGetVersion CurrentVersion => UpdateInstallation.GetCurrentVersion(Assembly.GetEntryAssembly());

        public string Distribution => UpdateInstallation.GetCurrentDistribution();

        public string RuntimeIdentifier => UpdatePackageNaming.GetCurrentRuntimeIdentifier();

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public MandatoryUpdateState LoadState()
        {
            return _stateStore.Load();
        }

        public void SaveState(MandatoryUpdateState state)
        {
            _stateStore.Save(state);
        }

        public async Task<UpdateMetadata> GetLatestMetadataAsync(CancellationToken cancellationToken)
        {
            using ECDsa trustedKey = TrustedUpdateSigningKey.Load();
            using var client = new UpdateMetadataClient(UpdateInstallation.GetRepository(), trustedKey);
            UpdateMetadataResult result = await client.GetLatestAsync(cancellationToken).ConfigureAwait(false);
            Logging.LogMessage($"Trusted update metadata {result.Metadata.Version} selected from " +
                               $"{result.SourceName} ({result.ValidSourceCount} valid source(s)).");
            return result.Metadata;
        }

        public async Task<string> DownloadUpdateAsync(UpdateMetadata metadata, UpdateAssetMetadata asset,
            IProgress<double> progress, CancellationToken cancellationToken)
        {
            string updateDirectory = Path.Combine(AppConfig.LocalApplicationDataPath, "Updates",
                metadata.Version);
            string packagePath = Path.Combine(updateDirectory, asset.Name);
            using var downloader = new UpdatePackageDownloader();
            return await downloader.DownloadAsync(UpdateInstallation.GetRepository(), metadata.Tag, asset,
                packagePath, progress, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> StartUpdaterAsync(UpdateMetadata metadata, UpdateAssetMetadata asset,
            string packagePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string installedUpdaterPath = Path.Combine(AppContext.BaseDirectory, Constants.UpdaterFileName);
            if (!File.Exists(installedUpdaterPath))
                throw new FileNotFoundException("The TouchPilot updater is missing.", installedUpdaterPath);

            string updaterDirectory = Path.GetDirectoryName(packagePath) ??
                                      throw new InvalidDataException("The update package path is invalid.");
            Directory.CreateDirectory(updaterDirectory);
            string temporaryUpdaterPath = Path.Combine(updaterDirectory,
                "TouchPilot.Updater." + metadata.Version + ".exe");
            File.Copy(installedUpdaterPath, temporaryUpdaterPath, true);

            var startInfo = new ProcessStartInfo
            {
                FileName = temporaryUpdaterPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = updaterDirectory
            };
            AddArgument(startInfo, "mode", asset.Distribution);
            AddArgument(startInfo, "package", packagePath);
            AddArgument(startInfo, "target", AppContext.BaseDirectory);
            AddArgument(startInfo, "restart", Constants.DaemonFileName);
            AddArgument(startInfo, "version", metadata.Version);
            AddArgument(startInfo, "sha256", asset.Sha256);
            AddArgument(startInfo, "wait-pid", Environment.ProcessId.ToString());

            try
            {
                using Process updater = Process.Start(startInfo) ??
                                        throw new InvalidOperationException(
                                            "The TouchPilot updater could not be started.");
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                return false;
            }

            try
            {
                await NamedPipe.SendMessageAsync(IpcCommands.Exit, Constants.ControlPanel, wait: false)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Logging.LogException(exception);
            }

            return true;
        }

        public void ExitApplication()
        {
            _uiContext.Post(_ => Application.Exit(), null);
        }

        public void LogException(Exception exception)
        {
            Logging.LogException(exception);
        }

        public void ShowUpdateProgress(UpdateProgressStage stage, string version, double percentage)
        {
            _uiContext.Post(_ =>
            {
                if (_updateProgressForm == null || _updateProgressForm.IsDisposed)
                {
                    _updateProgressForm = new UpdateProgressForm();
                    _updateProgressForm.FormClosed += (sender, args) => _updateProgressForm = null;
                }

                _updateProgressForm.SetProgress(stage, version, percentage);
                if (!_updateProgressForm.Visible)
                    _updateProgressForm.Show();
            }, null);
        }

        public void CloseUpdateWindow()
        {
            _uiContext.Post(_ =>
            {
                if (_updateProgressForm == null || _updateProgressForm.IsDisposed)
                    return;
                _updateProgressForm.CloseForApplication();
                _updateProgressForm = null;
            }, null);
        }

        public void ShowManualCheckResult(ManualUpdateCheckResult result)
        {
            _uiContext.Post(_ =>
            {
                string messageKey = result switch
                {
                    ManualUpdateCheckResult.Current => "Update.Current",
                    ManualUpdateCheckResult.Unsupported => "Update.Unsupported",
                    ManualUpdateCheckResult.AlreadyRunning => "Update.AlreadyRunning",
                    _ => "Update.Unavailable"
                };
                MessageBoxIcon icon = result == ManualUpdateCheckResult.Current
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning;
                MessageBox.Show(LocalizationProvider.Instance.GetTextValue(messageKey),
                    LocalizationProvider.Instance.GetTextValue("Update.Title"), MessageBoxButtons.OK, icon);
            }, null);
        }

        private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
        {
            startInfo.ArgumentList.Add("--" + name);
            startInfo.ArgumentList.Add(value);
        }
    }
}

using GestureSign.Common;
using GestureSign.Common.Configuration;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.Updates;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NuGet.Versioning;

namespace GestureSign.Daemon.Updates
{
    internal sealed class UpdateCoordinator : IDisposable
    {
        private readonly SynchronizationContext _uiContext;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private int _checking;

        public UpdateCoordinator(SynchronizationContext uiContext)
        {
            _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
        }

        public void ScheduleStartupCheck()
        {
            if (!UpdateInstallation.IsSelfUpdateSupported)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _shutdown.Token).ConfigureAwait(false);
                    await CheckForUpdateAsync(_shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                }
            });
        }

        public void Dispose()
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
        }

        private async Task CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _checking, 1) != 0)
                return;

            bool updateAccepted = false;
            try
            {
                NuGetVersion currentVersion = UpdateInstallation.GetCurrentVersion(Assembly.GetEntryAssembly());
                GitHubRepository repository = UpdateInstallation.GetRepository();

                using var client = new GitHubReleaseClient(repository);
                GitHubReleaseInfo release = await client.GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
                if (VersionComparer.VersionRelease.Compare(release.Version, currentVersion) <= 0)
                    return;

                string releaseVersion = ReleaseVersion.ToReleaseString(release.Version);
                string runtime = UpdatePackageNaming.GetCurrentRuntimeIdentifier();
                string packageAssetName = UpdatePackageNaming.GetAssetName(releaseVersion, runtime);
                string checksumAssetName = UpdatePackageNaming.GetChecksumAssetName(releaseVersion, runtime);
                GitHubReleaseAsset packageAsset = UpdatePackageNaming.FindAsset(release, packageAssetName);
                GitHubReleaseAsset checksumAsset = UpdatePackageNaming.FindAsset(release, checksumAssetName);
                if (packageAsset == null || checksumAsset == null)
                {
                    Logging.LogMessage($"Release {release.TagName} does not contain {packageAssetName} and its checksum.");
                    return;
                }

                DialogResult result = await InvokeOnUiAsync(() => MessageBox.Show(
                    BuildUpdatePrompt(release, releaseVersion),
                    LocalizationProvider.Instance.GetTextValue("Update.Title"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1));
                if (result != DialogResult.Yes)
                    return;

                updateAccepted = true;
                DownloadedPackage package = await DownloadAndVerifyAsync(client, packageAsset, checksumAsset,
                    releaseVersion, cancellationToken).ConfigureAwait(false);
                await StartUpdaterAsync(package, releaseVersion).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Logging.LogException(exception);
                if (updateAccepted)
                {
                    await InvokeOnUiAsync(() => MessageBox.Show(
                        exception.Message,
                        LocalizationProvider.Instance.GetTextValue("Update.FailedTitle"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error));
                }
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        }

        private static string BuildUpdatePrompt(GitHubReleaseInfo release, string releaseVersion)
        {
            string message = string.Format(
                LocalizationProvider.Instance.GetTextValue("Update.Available"), releaseVersion);
            if (string.IsNullOrWhiteSpace(release.Body))
                return message;

            string notes = release.Body.Trim();
            if (notes.Length > 1200)
                notes = notes.Substring(0, 1200) + Environment.NewLine + "...";
            return message + Environment.NewLine + Environment.NewLine + notes;
        }

        private static async Task<DownloadedPackage> DownloadAndVerifyAsync(GitHubReleaseClient client,
            GitHubReleaseAsset packageAsset, GitHubReleaseAsset checksumAsset, string releaseVersion,
            CancellationToken cancellationToken)
        {
            string updateDirectory = Path.Combine(AppConfig.LocalApplicationDataPath, "Updates", releaseVersion);
            Directory.CreateDirectory(updateDirectory);

            string packagePath = Path.Combine(updateDirectory, packageAsset.Name);
            string temporaryPackagePath = packagePath + ".download";
            string checksum = await client.DownloadStringAsync(checksumAsset.DownloadUrl, cancellationToken)
                .ConfigureAwait(false);
            string expectedHash = ParseChecksum(checksum);

            if (File.Exists(packagePath) &&
                string.Equals(ComputeSha256(packagePath), expectedHash, StringComparison.OrdinalIgnoreCase))
                return new DownloadedPackage(packagePath, expectedHash);

            if (File.Exists(temporaryPackagePath))
                File.Delete(temporaryPackagePath);
            await client.DownloadFileAsync(packageAsset.DownloadUrl, temporaryPackagePath, null, cancellationToken)
                .ConfigureAwait(false);

            string actualHash = ComputeSha256(temporaryPackagePath);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaryPackagePath);
                throw new InvalidDataException("The downloaded update package failed SHA-256 verification.");
            }

            File.Move(temporaryPackagePath, packagePath, true);
            return new DownloadedPackage(packagePath, expectedHash);
        }

        private async Task StartUpdaterAsync(DownloadedPackage package, string releaseVersion)
        {
            string installedUpdaterPath = Path.Combine(AppContext.BaseDirectory, Constants.UpdaterFileName);
            if (!File.Exists(installedUpdaterPath))
                throw new FileNotFoundException(string.Format(
                    LocalizationProvider.Instance.GetTextValue("Messages.ComponentNotFoundMessage"),
                    installedUpdaterPath), installedUpdaterPath);

            string updaterDirectory = Path.GetDirectoryName(package.Path);
            string temporaryUpdaterPath = Path.Combine(updaterDirectory,
                "GestureSign.Updater." + releaseVersion + ".exe");
            File.Copy(installedUpdaterPath, temporaryUpdaterPath, true);

            var startInfo = new ProcessStartInfo
            {
                FileName = temporaryUpdaterPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = updaterDirectory
            };
            startInfo.ArgumentList.Add("--package");
            startInfo.ArgumentList.Add(package.Path);
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(AppContext.BaseDirectory);
            startInfo.ArgumentList.Add("--restart");
            startInfo.ArgumentList.Add(Constants.DaemonFileName);
            startInfo.ArgumentList.Add("--version");
            startInfo.ArgumentList.Add(releaseVersion);
            startInfo.ArgumentList.Add("--sha256");
            startInfo.ArgumentList.Add(package.Sha256);
            startInfo.ArgumentList.Add("--wait-pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

            try
            {
                using Process updater = Process.Start(startInfo);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                return;
            }

            await NamedPipe.SendMessageAsync(IpcCommands.Exit, Constants.ControlPanel, wait: false)
                .ConfigureAwait(false);
            _uiContext.Post(_ => Application.Exit(), null);
        }

        private Task<T> InvokeOnUiAsync<T>(Func<T> action)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _uiContext.Post(_ =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }, null);
            return completion.Task;
        }

        private static string ParseChecksum(string checksum)
        {
            string value = checksum?.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (value == null || value.Length != 64 || !value.All(Uri.IsHexDigit))
                throw new InvalidDataException("The release checksum file is invalid.");
            return value;
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }

        private sealed class DownloadedPackage
        {
            public DownloadedPackage(string path, string sha256)
            {
                Path = path;
                Sha256 = sha256;
            }

            public string Path { get; }

            public string Sha256 { get; }
        }
    }
}

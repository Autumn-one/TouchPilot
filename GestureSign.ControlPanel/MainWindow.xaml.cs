using GestureSign.Common;
using GestureSign.Common.Configuration;
using GestureSign.Common.Localization;
using GestureSign.Common.Lifecycle;
using GestureSign.Common.Log;
using GestureSign.Common.Updates;
using GestureSign.ControlPanel.Common;
using GestureSign.ControlPanel.Dialogs;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace GestureSign.ControlPanel
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : TouchWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (CheckIfApplicationRunAsAdmin())
            {
                var result = MessageBox.Show(LocalizationProvider.Instance.GetTextValue("Messages.CompatWarning"),
                 LocalizationProvider.Instance.GetTextValue("Messages.CompatWarningTitle"), MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
            }
            await StartDaemonAsync();
            if (!IsLoaded)
                return;
            SetAboutInfo();
            Activate();

            DateTime? latestErrorTime = await Task.Run(FindLatestGestureSignErrorTime);
            if (!IsLoaded || latestErrorTime == null || AppConfig.LastErrorTime.CompareTo(latestErrorTime.Value) >= 0)
                return;

            AppConfig.LastErrorTime = latestErrorTime.Value;
            if (AppConfig.SendErrorReport)
                await Dispatcher.InvokeAsync(SendLog, DispatcherPriority.Background);
        }

        private void SetAboutInfo()
        {
            string version = LocalizationProvider.Instance.GetTextValue("About.Version") +
                             UpdateInstallation.GetCurrentVersionText(Application.ResourceAssembly);
            string releaseDate = LocalizationProvider.Instance.GetTextValue("About.ReleaseDate") +
                                 new DateTime(2000, 1, 1).AddDays(Application.ResourceAssembly.GetName().Version.Build)
                                     .AddSeconds(Application.ResourceAssembly.GetName().Version.Revision * 2);
            this.AboutTextBox.Text = this.AboutTextBox.Text.Insert(0, version + "\r\n" + releaseDate + "\r\n");
        }

        private void EdgeTouch_WindowDragRequested(object sender, EventArgs e)
        {
            WindowDragTab.IsSelected = true;
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var commandSource = sender as ICommandSource;
                var uri = commandSource?.CommandParameter as string;
                if (uri != null)
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                Logging.LogException(exception);
                MessageBox.Show(exception.Message, LocalizationProvider.Instance.GetTextValue("Messages.Error"));
            }
        }

        private void SendFeedback_Click(object sender, RoutedEventArgs e)
        {
            SendFeedback();
        }

        private DateTime? FindLatestGestureSignErrorTime()
        {
            using EventLog logs = new EventLog { Log = "Application" };
            var now = DateTime.Now;
            var entryCollection = logs.Entries;
            int logCount = entryCollection.Count;
            for (int i = logCount - 1; i > logCount - 500 && i < logCount && i >= 0; i--)
            {
                var entry = entryCollection[i];
                if (now.Subtract(entry.TimeWritten).TotalHours > 1)
                    break;

                if (entry.EntryType == EventLogEntryType.Error && ".NET Runtime".Equals(entry.Source) &&
                    entry.Message.IndexOf("GestureSign", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return entry.TimeWritten;
                }
                //The collection is dynamic and the number of entries may not be immutable
                logCount = entryCollection.Count;
            }
            return null;
        }

        private void SendLog()
        {
            var dialogResult = this.ShowModalMessageExternal(LocalizationProvider.Instance.GetTextValue("About.SendLogTitle"),
            LocalizationProvider.Instance.GetTextValue("Messages.FindNewErrorLog"),
            MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings()
            {
                AffirmativeButtonText = LocalizationProvider.Instance.GetTextValue("About.SendButton"),
                NegativeButtonText = LocalizationProvider.Instance.GetTextValue("About.DontSendButton"),
            });
            if (dialogResult == MessageDialogResult.Negative) return;
            SendFeedback();
        }

        private async void SendFeedback()
        {
            var controller =
                await this.ShowProgressAsync(LocalizationProvider.Instance.GetTextValue("About.Waiting"),
                            LocalizationProvider.Instance.GetTextValue("About.Exporting"));
            controller.SetIndeterminate();

            string result;
            try
            {
                result = await Task.Run(Log.Feedback.OutputLog);
            }
            catch (Exception exception)
            {
                Logging.LogException(exception);
                await controller.CloseAsync();
                this.ShowModalMessageExternal(LocalizationProvider.Instance.GetTextValue("Messages.Error"),
                    exception.Message);
                return;
            }
            await controller.CloseAsync();

            var logWin = new LogWindow(result) { Owner = this };
            if (logWin.ShowDialog() == true)
            {
                this.ShowModalMessageExternal(LocalizationProvider.Instance.GetTextValue("About.ExportSuccessTitle"),
                    LocalizationProvider.Instance.GetTextValue("About.ExportSuccess") + Environment.NewLine +
                    LocalizationProvider.Instance.GetTextValue("About.Contact"));
            }
        }

        private bool CheckIfApplicationRunAsAdmin()
        {
            string controlPanelRecord;
            string daemonRecord;
            using (RegistryKey layers = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"))
            {
                string controlPanelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.ControlPanelFileName);
                string daemonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.DaemonFileName);

                controlPanelRecord = layers?.GetValue(controlPanelPath) as string;
                daemonRecord = layers?.GetValue(daemonPath) as string;
            }

            return controlPanelRecord != null && controlPanelRecord.ToUpper().Contains("RUNASADMIN") ||
                   daemonRecord != null && daemonRecord.ToUpper().Contains("RUNASADMIN");
        }

        private async Task StartDaemonAsync()
        {
            string daemonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.DaemonFileName);
            if (!File.Exists(daemonPath))
            {
                MessageBox.Show(LocalizationProvider.Instance.GetTextValue("Messages.CannotFindDaemonMessage"),
                    LocalizationProvider.Instance.GetTextValue("Messages.Error"), MessageBoxButton.OK,
                    MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                return;
            }

            bool requestElevation = AppConfig.RunAsAdmin && !AppConfig.UiAccess;
            if (CheckRunningDaemon(requestElevation))
                return;
            try
            {
                if (requestElevation && !DaemonStartup.IsCurrentProcessElevated)
                {
                    try
                    {
                        if (await StartupHelper.TryStartHighPrivilegeDaemonAsync())
                            return;
                    }
                    catch (Exception)
                    {
                        // If the registered task is unavailable, Windows runas remains the fallback.
                    }
                }
                if (!IsLoaded || CheckRunningDaemon(requestElevation))
                    return;
                using Process daemon = Process.Start(DaemonStartup.CreateStartInfo(daemonPath,
                    requestElevation || DaemonStartup.IsCurrentProcessElevated));
                if (daemon == null)
                    throw new InvalidOperationException("Windows did not start the gesture service.");
            }
            catch (Exception e)
            {
                Logging.LogException(e);
                MessageBox.Show(e.Message + Environment.NewLine +
                    string.Format(LocalizationProvider.Instance.GetTextValue("Messages.StartupError"), daemonPath),
                    LocalizationProvider.Instance.GetTextValue("Messages.Error"), MessageBoxButton.OK,
                    MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
            }
        }

        private static bool CheckRunningDaemon(bool requestElevation)
        {
            if (!DaemonStartup.IsRunning())
                return false;
            if (requestElevation &&
                DaemonStartup.TryGetRunningElevation(AppContext.BaseDirectory, out bool elevated) && !elevated)
                MessageBox.Show(LocalizationProvider.Instance.GetTextValue("Messages.AdministratorRestartRequired"),
                    Constants.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
    }
}

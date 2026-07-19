using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Extensions;
using GestureSign.Common.Gestures;
using GestureSign.Common.Localization;
using GestureSign.ControlPanel.Common;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace GestureSign.ControlPanel.Dialogs
{
    /// <summary>
    /// Interaction logic for DownloadWindow.xaml
    /// </summary>
    public partial class DownloadWindow : TouchWindow
    {
        private CancellationTokenSource _downloadCancellationTokenSource;
        private bool _isClosed;
        private string _tempDirectory;

        private readonly string[] _source = new string[] { "https://transposony.coding.net/p/GestureSignSettings/d/GestureSignSettings/git/archive/master",
            "https://github.com/TransposonY/GestureSignSettings/archive/master.zip" };

        public DownloadWindow()
        {
            InitializeComponent();
            _tempDirectory = Path.Combine(AppConfig.LocalApplicationDataPath, "Temp");
        }

        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isClosed = false;
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _downloadCancellationTokenSource = cancellationTokenSource;
            var downloadTasks = _source
                .Select(url => DownloadSettingFileAsync(url, cancellationTokenSource.Token))
                .ToList();
            Exception lastError = null;

            try
            {
                while (downloadTasks.Count != 0)
                {
                    var completedTask = await Task.WhenAny(downloadTasks);
                    downloadTasks.Remove(completedTask);

                    try
                    {
                        var file = await completedTask;
                        if (file == null || file.Length == 0)
                            throw new InvalidDataException("The downloaded settings archive is empty.");

                        await Task.Run(() => LoadSettingFile(file), cancellationTokenSource.Token);
                        await cancellationTokenSource.CancelAsync();
                        ObserveRemainingDownloads(downloadTasks);
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
                    {
                        // The window was closed or the shared download timeout elapsed.
                    }
                    catch (Exception exception)
                    {
                        lastError = exception;
                    }
                }

                if (_isClosed)
                    return;

                if (cancellationTokenSource.IsCancellationRequested)
                    lastError = new TimeoutException();

                if (lastError != null)
                    this.ShowModalMessageExternal(lastError.GetType().Name, lastError.Message);
            }
            finally
            {
                if (ReferenceEquals(_downloadCancellationTokenSource, cancellationTokenSource))
                    _downloadCancellationTokenSource = null;
            }
        }

        private static async Task<byte[]> DownloadSettingFileAsync(string url, CancellationToken cancellationToken)
        {
            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 6.3; Trident/7.0; .NET4.0E; .NET4.0C; rv:11.0) like Gecko");
            return await client.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        }

        private static void ObserveRemainingDownloads(IEnumerable<Task<byte[]>> downloadTasks)
        {
            foreach (var downloadTask in downloadTasks)
            {
                _ = downloadTask.ContinueWith(
                    completedTask => _ = completedTask.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
        }

        private async void MetroWindow_Closed(object sender, EventArgs e)
        {
            _isClosed = true;
            var cancellationTokenSource = _downloadCancellationTokenSource;
            if (cancellationTokenSource != null)
                await cancellationTokenSource.CancelAsync();
        }

        private void LoadSettingFile(byte[] file)
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
            Directory.CreateDirectory(_tempDirectory);

            string filePath = Path.Combine(_tempDirectory, "Setting.zip");

            File.WriteAllBytes(filePath, file);
            ZipFile.ExtractToDirectory(filePath, _tempDirectory);

            var newApps = new List<IApplication>();
            var gestures = new List<IGesture>();
            foreach (string settingFile in Directory.GetFiles(_tempDirectory, "*.*", SearchOption.AllDirectories))
            {
                switch (Path.GetExtension(settingFile))
                {
                    case GestureSign.Common.Constants.ActionExtension:
                        var currentApps = FileManager.LoadObject<List<IApplication>>(settingFile, false, true);
                        if (currentApps != null)
                        {
                            newApps.AddRange(currentApps);
                        }
                        break;
                    case GestureSign.Common.Constants.GesturesExtension:
                        var currentGestures = GestureManager.LoadGesturesFromFile(settingFile);
                        if (currentGestures != null)
                        {
                            gestures.AddRange(currentGestures);
                        }
                        break;
                }
            }

            Dispatcher.InvokeAsync(() =>
            {
                if (_isClosed)
                    return;

                ApplicationSelector.Initialize(newApps, gestures);
                ProgressRing.Visibility = Visibility.Collapsed;
                ApplicationSelector.Visibility = Visibility.Visible;
            }
            , DispatcherPriority.Input);

            File.Delete(filePath);
            Directory.Delete(_tempDirectory, true);
        }

        private void FromFileButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog ofdApplications = new Microsoft.Win32.OpenFileDialog()
            {
                Filter = $"{LocalizationProvider.Instance.GetTextValue("Action.ArchiveFile")}|*{GestureSign.Common.Constants.ActionExtension};*{GestureSign.Common.Constants.ArchivesExtension}",
                Title = LocalizationProvider.Instance.GetTextValue("Common.Import"),
                CheckFileExists = true
            };
            if (ofdApplications.ShowDialog().Value)
            {
                try
                {
                    switch (Path.GetExtension(ofdApplications.FileName).ToLower())
                    {
                        case GestureSign.Common.Constants.ActionExtension:
                            var newApps = FileManager.LoadObject<List<IApplication>>(ofdApplications.FileName, false, true, true);
                            if (newApps != null)
                            {
                                Hide();
                                ExportImportDialog exportImportDialog = new ExportImportDialog(false, false, newApps, GestureManager.Instance.Gestures);
                                exportImportDialog.ShowDialog();
                                Close();
                            }
                            break;
                        case GestureSign.Common.Constants.ArchivesExtension:
                            {
                                IEnumerable<IApplication> applications;
                                IEnumerable<IGesture> gestures;
                                Archive.LoadFromArchive(ofdApplications.FileName, out applications, out gestures);
                                if (applications != null && gestures != null)
                                {
                                    Hide();
                                    ExportImportDialog exportImportDialog = new ExportImportDialog(false, false, applications, gestures);
                                    exportImportDialog.ShowDialog();
                                    Close();
                                }
                                break;
                            }
                    }
                }
                catch (Exception exception)
                {
                    this.ShowModalMessageExternal(LocalizationProvider.Instance.GetTextValue("Messages.Error"), exception.Message);
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            int newActionCount = 0;
            List<IApplication> newApplications = new List<IApplication>();
            var seletedApplications = ApplicationSelector.SeletedApplications;

            var gestures = seletedApplications.GetRelatedGestures(ApplicationSelector.GestureMap.Values.Select(gi => gi.Gesture));
            GestureManager.Instance.ImportGestures(gestures, seletedApplications);

            foreach (IApplication newApp in seletedApplications)
            {
                if (newApp is IgnoredApp)
                {
                    var matchApp = ApplicationManager.Instance.FindMatchApplications<IgnoredApp>(newApp.MatchUsing, newApp.MatchString);
                    if (matchApp.Length == 0)
                    {
                        newApplications.Add(newApp);
                    }
                }
                else
                {
                    var existingApp = ApplicationManager.Instance.Applications.Find(app => !(app is IgnoredApp) && app.MatchUsing == newApp.MatchUsing && app.MatchString == newApp.MatchString);
                    if (existingApp != null)
                    {
                        foreach (IAction newAction in newApp.Actions)
                        {
                            existingApp.AddAction(newAction);
                            newActionCount++;
                        }
                    }
                    else
                    {
                        newActionCount += newApp.Actions.Count();
                        newApplications.Add(newApp);
                    }
                }
            }
            if (newApplications.Count != 0)
            {
                ApplicationManager.Instance.AddApplicationRange(newApplications);
            }
            if (newApplications.Count + newActionCount != 0)
                ApplicationManager.Instance.SaveApplications();

            this.ShowModalMessageExternal(LocalizationProvider.Instance.GetTextValue("ExportImportDialog.ImportCompleteTitle"),
                String.Format(LocalizationProvider.Instance.GetTextValue("ExportImportDialog.ImportComplete"), newActionCount, newApplications.Count));
            Close();
        }
    }
}

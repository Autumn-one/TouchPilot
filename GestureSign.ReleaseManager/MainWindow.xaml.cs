using GestureSign.Common.Updates;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NuGet.Versioning;
using Forms = System.Windows.Forms;

namespace GestureSign.ReleaseManager
{
    public partial class MainWindow : Window
    {
        private readonly ReleaseBuildService _buildService = new ReleaseBuildService();
        private CancellationTokenSource _operationCancellation;
        private bool _updatingReleaseTitle;

        public MainWindow()
        {
            InitializeComponent();
            string sourceDirectory = FindRepositoryRoot() ?? Environment.CurrentDirectory;
            RepositoryTextBox.Text = "Autumn-one/TouchPilot";
            SourceDirectoryTextBox.Text = sourceDirectory;
            LoadUserConfiguration(sourceDirectory);
        }

        private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "选择包含 build-release.ps1 的 TouchPilot 源码目录",
                SelectedPath = SourceDirectoryTextBox.Text,
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                SourceDirectoryTextBox.Text = dialog.SelectedPath;
                LoadUserConfiguration(dialog.SelectedPath);
            }
        }

        private void VersionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingReleaseTitle || ReleaseTitleTextBox == null)
                return;

            _updatingReleaseTitle = true;
            ReleaseTitleTextBox.Text = "TouchPilot " + VersionTextBox.Text.Trim();
            _updatingReleaseTitle = false;
        }

        private async void PublishButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PublishRequest request = ValidateRequest();
                SetBusyState(true, "正在检查 Release 状态...");
                LogTextBox.Clear();
                _operationCancellation = new CancellationTokenSource();
                CancellationToken cancellationToken = _operationCancellation.Token;

                var progress = new Progress<string>(AppendLog);
                using var publisher = new GitHubReleasePublisher(request.Repository, request.Token);
                string tagName = "v" + request.Version;
                await publisher.EnsureCanPublishAsync(tagName, cancellationToken);

                SetBusyState(true, "正在测试并构建发布资产...");
                ReleaseBuildResult build = await _buildService.BuildReleaseAsync(request.SourceDirectory,
                    request.Version, request.Repository.Slug, request.ReleaseNotes, progress,
                    cancellationToken);

                AppendLog("Tests, assets, and signed metadata completed.");
                SetBusyState(true, "正在上传 GitHub Release...");
                GitHubReleaseInfo release = await publisher.PublishImmutableAsync(tagName,
                    request.ReleaseTitle, request.ReleaseNotes, request.Draft, build.AssetPaths, progress,
                    cancellationToken);

                StatusTextBlock.Text = (release.Draft ? "草稿完成：" : "发布完成：") + release.TagName;
                MessageBox.Show(this,
                    (release.Draft ? "Release 草稿已构建并上传。" : "Release 已成功发布。") +
                    "\n\n" + release.HtmlUrl, release.Draft ? "草稿完成" : "发布完成",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                AppendLog("Release operation canceled.");
                StatusTextBlock.Text = "发布已取消";
            }
            catch (Exception exception)
            {
                AppendLog("FAILED: " + exception);
                StatusTextBlock.Text = "发布失败";
                MessageBox.Show(this, exception.Message, "发布失败", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _operationCancellation?.Dispose();
                _operationCancellation = null;
                SetBusyState(false, StatusTextBlock.Text);
            }
        }

        private async void DeleteReleaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ReleaseIdentity identity = ValidateReleaseIdentity();
                string tagName = "v" + identity.Version;
                MessageBoxResult confirmation = MessageBox.Show(this,
                    $"确认删除 {identity.Repository.Slug} 中的 Release {tagName}？\n\n" +
                    $"Release 资产也会删除，但 Git 标签 {tagName} 会保留。此操作不可撤销。",
                    "确认删除 Release", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirmation != MessageBoxResult.Yes)
                    return;

                SetBusyState(true, "正在删除 GitHub Release...");
                LogTextBox.Clear();
                _operationCancellation = new CancellationTokenSource();
                var progress = new Progress<string>(AppendLog);
                using var publisher = new GitHubReleasePublisher(identity.Repository, identity.Token);
                await publisher.DeleteAsync(tagName, progress, _operationCancellation.Token);

                StatusTextBlock.Text = "已删除：" + tagName;
                MessageBox.Show(this, $"Release {tagName} 已删除。\n\nGit 标签 {tagName} 已保留。",
                    "删除完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                AppendLog("Release deletion canceled.");
                StatusTextBlock.Text = "删除已取消";
            }
            catch (Exception exception)
            {
                AppendLog("FAILED: " + exception);
                StatusTextBlock.Text = "删除失败";
                MessageBox.Show(this, exception.Message, "删除失败", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _operationCancellation?.Dispose();
                _operationCancellation = null;
                SetBusyState(false, StatusTextBlock.Text);
            }
        }

        private PublishRequest ValidateRequest()
        {
            ReleaseIdentity identity = ValidateReleaseIdentity();

            string sourceDirectory = Path.GetFullPath(SourceDirectoryTextBox.Text.Trim());
            if (!File.Exists(Path.Combine(sourceDirectory, "build-release.ps1")))
                throw new InvalidOperationException("源码目录中没有 build-release.ps1。");

            return new PublishRequest
            {
                Repository = identity.Repository,
                Token = identity.Token,
                SourceDirectory = sourceDirectory,
                Version = identity.Version,
                ReleaseTitle = string.IsNullOrWhiteSpace(ReleaseTitleTextBox.Text)
                    ? "TouchPilot " + identity.Version
                    : ReleaseTitleTextBox.Text.Trim(),
                ReleaseNotes = ReleaseNotesTextBox.Text,
                Draft = DraftCheckBox.IsChecked == true
            };
        }

        private ReleaseIdentity ValidateReleaseIdentity()
        {
            if (string.IsNullOrWhiteSpace(TokenPasswordBox.Password))
                LoadUserConfiguration(SourceDirectoryTextBox.Text);
            GitHubRepository repository = GitHubRepository.Parse(RepositoryTextBox.Text);
            string token = TokenPasswordBox.Password;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("请填写 GitHub 授权密钥。");

            NuGetVersion version = ReleaseVersion.Parse(VersionTextBox.Text);
            return new ReleaseIdentity
            {
                Repository = repository,
                Token = token,
                Version = ReleaseVersion.ToReleaseString(version)
            };
        }

        private void LoadUserConfiguration(string sourceDirectory)
        {
            try
            {
                ReleaseManagerUserConfiguration configuration =
                    ReleaseManagerUserConfiguration.TryLoad(sourceDirectory);
                if (configuration == null)
                    return;

                RepositoryTextBox.Text = configuration.Repository;
                TokenPasswordBox.Password = configuration.Token;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is InvalidDataException)
            {
                StatusTextBlock.Text = "发布配置无效：" + exception.Message;
            }
        }

        private void SetBusyState(bool busy, string status)
        {
            PublishButton.IsEnabled = !busy;
            DeleteReleaseButton.IsEnabled = !busy;
            PublishProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            StatusTextBlock.Text = status;
        }

        private void AppendLog(string message)
        {
            LogTextBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message +
                                  Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            _operationCancellation?.Cancel();
            base.OnClosed(e);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "build-release.ps1")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }

        private sealed class PublishRequest
        {
            public GitHubRepository Repository { get; set; }
            public string Token { get; set; }
            public string SourceDirectory { get; set; }
            public string Version { get; set; }
            public string ReleaseTitle { get; set; }
            public string ReleaseNotes { get; set; }
            public bool Draft { get; set; }
        }

        private sealed class ReleaseIdentity
        {
            public GitHubRepository Repository { get; set; }
            public string Token { get; set; }
            public string Version { get; set; }
        }
    }
}

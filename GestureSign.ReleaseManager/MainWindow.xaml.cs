using GestureSign.Common;
using GestureSign.Common.Updates;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace GestureSign.ReleaseManager
{
    public partial class MainWindow : Window
    {
        private readonly ReleaseBuildService _buildService = new ReleaseBuildService();
        private bool _updatingReleaseTitle;

        public MainWindow()
        {
            InitializeComponent();
            RepositoryTextBox.Text = Constants.DefaultGitHubRepository;
            SourceDirectoryTextBox.Text = FindRepositoryRoot() ?? Environment.CurrentDirectory;
            SelectRuntime(UpdatePackageNaming.GetCurrentRuntimeIdentifier());
        }

        private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "选择包含 publish.ps1 的 GestureSign 源码目录",
                SelectedPath = SourceDirectoryTextBox.Text,
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
                SourceDirectoryTextBox.Text = dialog.SelectedPath;
        }

        private void VersionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingReleaseTitle || ReleaseTitleTextBox == null)
                return;

            _updatingReleaseTitle = true;
            ReleaseTitleTextBox.Text = "GestureSign " + VersionTextBox.Text.Trim();
            _updatingReleaseTitle = false;
        }

        private async void PublishButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PublishRequest request = ValidateRequest();
                SetBusyState(true, "正在构建发布包...");
                LogTextBox.Clear();

                var progress = new Progress<string>(AppendLog);
                await _buildService.BuildAsync(request.SourceDirectory, request.Configuration,
                    request.Runtime, request.Version, request.Repository.Slug, request.PackagePath, progress,
                    CancellationToken.None);

                AppendLog("Build and package completed.");
                SetBusyState(true, "正在上传 GitHub Release...");
                using var publisher = new GitHubReleasePublisher(request.Repository, request.Token);
                GitHubReleaseInfo release = await publisher.PublishAsync("v" + request.Version,
                    request.ReleaseTitle, request.ReleaseNotes, request.Draft, request.Prerelease,
                    new[] { request.PackagePath, request.PackagePath + ".sha256" }, progress,
                    CancellationToken.None);

                StatusTextBlock.Text = "发布完成：" + release.TagName;
                MessageBox.Show(this, "Release 已成功创建或更新。\n\n" + release.HtmlUrl,
                    "发布完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
                var progress = new Progress<string>(AppendLog);
                using var publisher = new GitHubReleasePublisher(identity.Repository, identity.Token);
                await publisher.DeleteAsync(tagName, progress, CancellationToken.None);

                StatusTextBlock.Text = "已删除：" + tagName;
                MessageBox.Show(this, $"Release {tagName} 已删除。\n\nGit 标签 {tagName} 已保留。",
                    "删除完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
                SetBusyState(false, StatusTextBlock.Text);
            }
        }

        private PublishRequest ValidateRequest()
        {
            ReleaseIdentity identity = ValidateReleaseIdentity();

            string sourceDirectory = Path.GetFullPath(SourceDirectoryTextBox.Text.Trim());
            if (!File.Exists(Path.Combine(sourceDirectory, "publish.ps1")))
                throw new InvalidOperationException("源码目录中没有 publish.ps1。");

            string runtime = GetSelectedValue(RuntimeComboBox);
            string configuration = GetSelectedValue(ConfigurationComboBox);
            string packageDirectory = Path.Combine(sourceDirectory, "artifacts", "release-manager", "packages");
            string packagePath = Path.Combine(packageDirectory,
                UpdatePackageNaming.GetAssetName(identity.Version, runtime));

            return new PublishRequest
            {
                Repository = identity.Repository,
                Token = identity.Token,
                SourceDirectory = sourceDirectory,
                Version = identity.Version,
                Runtime = runtime,
                Configuration = configuration,
                PackagePath = packagePath,
                ReleaseTitle = string.IsNullOrWhiteSpace(ReleaseTitleTextBox.Text)
                    ? "GestureSign " + identity.Version
                    : ReleaseTitleTextBox.Text.Trim(),
                ReleaseNotes = ReleaseNotesTextBox.Text,
                Draft = DraftCheckBox.IsChecked == true,
                Prerelease = PrereleaseCheckBox.IsChecked == true
            };
        }

        private ReleaseIdentity ValidateReleaseIdentity()
        {
            GitHubRepository repository = GitHubRepository.Parse(RepositoryTextBox.Text);
            string token = TokenPasswordBox.Password;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("请填写 GitHub 授权密钥。");

            Version version = ReleaseVersion.Parse(VersionTextBox.Text);
            return new ReleaseIdentity
            {
                Repository = repository,
                Token = token,
                Version = ReleaseVersion.ToReleaseString(version)
            };
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

        private void SelectRuntime(string runtime)
        {
            foreach (ComboBoxItem item in RuntimeComboBox.Items)
            {
                if (string.Equals(item.Content?.ToString(), runtime, StringComparison.OrdinalIgnoreCase))
                {
                    RuntimeComboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private static string GetSelectedValue(ComboBox comboBox)
        {
            if (!(comboBox.SelectedItem is ComboBoxItem item) || item.Content == null)
                throw new InvalidOperationException("请选择构建选项。");
            return item.Content.ToString();
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "publish.ps1")))
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
            public string Runtime { get; set; }
            public string Configuration { get; set; }
            public string PackagePath { get; set; }
            public string ReleaseTitle { get; set; }
            public string ReleaseNotes { get; set; }
            public bool Draft { get; set; }
            public bool Prerelease { get; set; }
        }

        private sealed class ReleaseIdentity
        {
            public GitHubRepository Repository { get; set; }
            public string Token { get; set; }
            public string Version { get; set; }
        }
    }
}

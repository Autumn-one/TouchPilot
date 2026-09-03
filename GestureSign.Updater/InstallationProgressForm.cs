using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace GestureSign.Updater
{
    internal sealed class InstallationProgressForm : Form
    {
        private readonly Label _statusLabel;
        private readonly Label _detailLabel;
        private readonly ProgressBar _progressBar;
        private readonly Button _retryButton;
        private readonly string _version;
        private bool _allowClose;

        public InstallationProgressForm(string version)
        {
            _version = version ?? string.Empty;
            Text = Localized("TouchPilot 更新", "TouchPilot Update");
            AccessibleName = Text;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.White;
            ClientSize = new Size(480, 226);
            ControlBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;

            var titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                Height = 42,
                Text = "TouchPilot",
                TextAlign = ContentAlignment.MiddleLeft
            };
            _statusLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 10),
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _progressBar = new ProgressBar
            {
                AccessibleName = Localized("安装进度", "Installation progress"),
                Dock = DockStyle.Top,
                Height = 18,
                Maximum = 100,
                Minimum = 0,
                Style = ProgressBarStyle.Marquee
            };
            _detailLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(88, 96, 105),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _retryButton = new Button
            {
                AutoSize = true,
                MinimumSize = new Size(86, 32),
                Text = Localized("重试", "Retry"),
                UseVisualStyleBackColor = true,
                Visible = false
            };
            _retryButton.Click += (sender, args) =>
            {
                _retryButton.Visible = false;
                RetryRequested?.Invoke(this, EventArgs.Empty);
            };

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0),
                WrapContents = false
            };
            actions.Controls.Add(_retryButton);

            var content = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 22, 28, 20),
                RowCount = 5
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            content.Controls.Add(titleLabel, 0, 0);
            content.Controls.Add(_statusLabel, 0, 1);
            content.Controls.Add(_progressBar, 0, 2);
            content.Controls.Add(_detailLabel, 0, 3);
            content.Controls.Add(actions, 0, 4);
            Controls.Add(content);

            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
            Location = GetCenteredLocation(workingArea, Size);
        }

        public event EventHandler RetryRequested;

        public void ShowPreparing()
        {
            RunOnUiThread(() =>
            {
                _retryButton.Visible = false;
                _progressBar.Style = ProgressBarStyle.Marquee;
                _statusLabel.Text = FormatLocalized("正在准备安装 TouchPilot {0}",
                    "Preparing to install TouchPilot {0}", _version);
                _detailLabel.Text = Localized("正在等待 TouchPilot 退出", "Waiting for TouchPilot to close");
            });
        }

        public void ShowInstalling(UpdateMode mode)
        {
            RunOnUiThread(() =>
            {
                _statusLabel.Text = FormatLocalized("正在安装 TouchPilot {0}",
                    "Installing TouchPilot {0}", _version);
                _detailLabel.Text = Localized("请保持此窗口打开", "Keep this window open");
                _progressBar.Style = mode == UpdateMode.Portable
                    ? ProgressBarStyle.Continuous
                    : ProgressBarStyle.Marquee;
                if (mode == UpdateMode.Portable)
                    _progressBar.Value = 0;
            });
        }

        public void ReportProgress(double percentage)
        {
            RunOnUiThread(() =>
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Value = ClampPercentage(percentage);
                _detailLabel.Text = FormatLocalized("已完成 {0}%", "{0}% complete",
                    _progressBar.Value);
            });
        }

        public void ShowCompleted()
        {
            RunOnUiThread(() =>
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Value = 100;
                _statusLabel.Text = Localized("更新完成", "Update complete");
                _detailLabel.Text = Localized("正在启动 TouchPilot", "Starting TouchPilot");
            });
        }

        public void ShowRetry(string message)
        {
            RunOnUiThread(() =>
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Value = 0;
                _statusLabel.Text = Localized("更新暂时无法完成", "The update could not finish");
                _detailLabel.Text = string.IsNullOrWhiteSpace(message)
                    ? Localized("请重试", "Try again")
                    : message.Replace(Environment.NewLine, " ");
                _retryButton.Visible = true;
                _retryButton.Focus();
            });
        }

        public void CloseForApplication()
        {
            RunOnUiThread(() =>
            {
                _allowClose = true;
                Close();
            });
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
            Location = GetCenteredLocation(workingArea, Size);
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
                e.Cancel = true;
            base.OnFormClosing(e);
        }

        internal static Point GetCenteredLocation(Rectangle workingArea, Size size)
        {
            return new Point(workingArea.Left + Math.Max(0, (workingArea.Width - size.Width) / 2),
                workingArea.Top + Math.Max(0, (workingArea.Height - size.Height) / 2));
        }

        private void RunOnUiThread(Action action)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }
            action();
        }

        private static int ClampPercentage(double value)
        {
            if (double.IsNaN(value) || value <= 0)
                return 0;
            if (value >= 100)
                return 100;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static string Localized(string chinese, string english)
        {
            return string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "zh",
                StringComparison.OrdinalIgnoreCase) ? chinese : english;
        }

        private static string FormatLocalized(string chinese, string english, object value)
        {
            return string.Format(CultureInfo.CurrentUICulture, Localized(chinese, english), value);
        }
    }
}

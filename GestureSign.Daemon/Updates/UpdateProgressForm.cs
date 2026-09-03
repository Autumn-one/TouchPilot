using GestureSign.Common.Localization;
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace GestureSign.Daemon.Updates
{
    internal sealed class UpdateProgressForm : Form
    {
        private readonly Label _statusLabel;
        private readonly Label _detailLabel;
        private readonly ProgressBar _progressBar;
        private bool _allowClose;

        public UpdateProgressForm()
        {
            Text = TextValue("Update.Title");
            AccessibleName = Text;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.White;
            ClientSize = new Size(480, 190);
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
                AccessibleName = TextValue("Update.Progress"),
                Dock = DockStyle.Top,
                Height = 18,
                Maximum = 100,
                Minimum = 0,
                Style = ProgressBarStyle.Continuous
            };
            _detailLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(88, 96, 105),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var content = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 22, 28, 20),
                RowCount = 4
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(titleLabel, 0, 0);
            content.Controls.Add(_statusLabel, 0, 1);
            content.Controls.Add(_progressBar, 0, 2);
            content.Controls.Add(_detailLabel, 0, 3);
            Controls.Add(content);

            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
            Location = GetCenteredLocation(workingArea, Size);
        }

        public void SetProgress(UpdateProgressStage stage, string version, double percentage)
        {
            string displayVersion = string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim();
            switch (stage)
            {
                case UpdateProgressStage.Downloading:
                    _progressBar.Style = ProgressBarStyle.Continuous;
                    _progressBar.Value = ClampPercentage(percentage);
                    _statusLabel.Text = FormatText("Update.Downloading", displayVersion);
                    _detailLabel.Text = FormatText("Update.DownloadProgress", _progressBar.Value);
                    break;
                case UpdateProgressStage.PreparingInstallation:
                    _progressBar.Style = ProgressBarStyle.Marquee;
                    _statusLabel.Text = FormatText("Update.PreparingInstallation", displayVersion);
                    _detailLabel.Text = TextValue("Update.KeepOpen");
                    break;
                default:
                    _progressBar.Style = ProgressBarStyle.Marquee;
                    _statusLabel.Text = FormatText("Update.RetryPending", displayVersion);
                    _detailLabel.Text = TextValue("Update.WillRetry");
                    break;
            }
        }

        public void CloseForApplication()
        {
            _allowClose = true;
            Close();
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

        private static int ClampPercentage(double value)
        {
            if (double.IsNaN(value) || value <= 0)
                return 0;
            if (value >= 100)
                return 100;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static string TextValue(string key)
        {
            return LocalizationProvider.Instance.GetTextValue(key);
        }

        private static string FormatText(string key, object value)
        {
            return string.Format(CultureInfo.CurrentUICulture, TextValue(key), value);
        }
    }
}

using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using System;
using System.IO;
using System.Text;
using System.Windows;

namespace GestureSign.ControlPanel.Dialogs
{
    /// <summary>
    /// Interaction logic for LogWindow.xaml
    /// </summary>
    public partial class LogWindow : MetroWindow
    {
        public LogWindow(string log)
        {
            InitializeComponent();
            LogTextBox.Text = log;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = LocalizationProvider.Instance.GetTextValue("About.SendLogTitle"),
                FileName = "TouchPilot-Feedback-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
                DefaultExt = ".txt",
                Filter = LocalizationProvider.Instance.GetTextValue("About.LogFileFilter")
            };
            if (saveDialog.ShowDialog(this) != true)
                return;

            try
            {
                File.WriteAllText(saveDialog.FileName,
                    MessageTextBox.Text + Environment.NewLine + LogTextBox.Text,
                    new UTF8Encoding(false));
                DialogResult = true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Logging.LogException(exception);
                MessageBox.Show(this, exception.Message,
                    LocalizationProvider.Instance.GetTextValue("Messages.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

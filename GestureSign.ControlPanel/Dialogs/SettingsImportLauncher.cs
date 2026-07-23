using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Localization;
using GestureSign.ControlPanel.Common;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace GestureSign.ControlPanel.Dialogs
{
    internal static class SettingsImportLauncher
    {
        public static void Show()
        {
            var fileDialog = new OpenFileDialog
            {
                Filter = $"{LocalizationProvider.Instance.GetTextValue("Action.ArchiveFile")}|" +
                         $"*{GestureSign.Common.Constants.ActionExtension};" +
                         $"*{GestureSign.Common.Constants.ArchivesExtension}",
                Title = LocalizationProvider.Instance.GetTextValue("Common.Import"),
                CheckFileExists = true
            };
            if (fileDialog.ShowDialog() != true)
                return;

            try
            {
                if (!TryLoad(fileDialog.FileName, out IEnumerable<IApplication> applications,
                        out IEnumerable<IGesture> gestures))
                    return;

                var importDialog = new ExportImportDialog(false, false, applications, gestures);
                importDialog.ShowDialog();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message,
                    LocalizationProvider.Instance.GetTextValue("Messages.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool TryLoad(string fileName, out IEnumerable<IApplication> applications,
            out IEnumerable<IGesture> gestures)
        {
            applications = null;
            gestures = null;
            switch (Path.GetExtension(fileName).ToLowerInvariant())
            {
                case GestureSign.Common.Constants.ActionExtension:
                    applications = FileManager.LoadObject<List<IApplication>>(fileName,
                        false, true, true);
                    gestures = GestureManager.Instance.Gestures;
                    break;
                case GestureSign.Common.Constants.ArchivesExtension:
                    Archive.LoadFromArchive(fileName, out applications, out gestures);
                    break;
                default:
                    return false;
            }

            return applications != null && gestures != null;
        }
    }
}

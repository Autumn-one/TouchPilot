using System;
using System.IO;

namespace GestureSign.Common.Configuration
{
    internal static class ProductDataMigration
    {
        internal const string LegacyProductName = "GestureSign";
        internal const string CurrentProductName = "TouchPilot";
        internal const string LegacyConfigFileName = LegacyProductName + ".config";
        internal const string CurrentConfigFileName = CurrentProductName + ".config";

        public static void MigrateInstalledData(string legacyRoamingDirectory,
            string currentRoamingDirectory, string legacyLocalDirectory, string currentLocalDirectory)
        {
            CopyDirectoryMissing(legacyRoamingDirectory, currentRoamingDirectory, relativePath =>
                !string.Equals(relativePath, LegacyConfigFileName, StringComparison.OrdinalIgnoreCase));
            CopyFileMissing(Path.Combine(legacyRoamingDirectory, LegacyConfigFileName),
                Path.Combine(currentRoamingDirectory, CurrentConfigFileName));

            string legacyBackupDirectory = Path.Combine(legacyLocalDirectory, "Backup");
            string currentBackupDirectory = Path.Combine(currentLocalDirectory, "Backup");
            CopyDirectoryMissing(legacyBackupDirectory, currentBackupDirectory, _ => true);
        }

        public static void MigratePortableData(string applicationDataDirectory)
        {
            CopyFileMissing(Path.Combine(applicationDataDirectory, LegacyConfigFileName),
                Path.Combine(applicationDataDirectory, CurrentConfigFileName));
        }

        private static void CopyDirectoryMissing(string sourceDirectory, string destinationDirectory,
            Func<string, bool> includeFile)
        {
            if (!Directory.Exists(sourceDirectory))
                return;

            CopyDirectoryMissing(sourceDirectory, sourceDirectory, destinationDirectory, includeFile);
        }

        private static void CopyDirectoryMissing(string sourceRoot, string sourceDirectory,
            string destinationRoot, Func<string, bool> includeFile)
        {
            var sourceInfo = new DirectoryInfo(sourceDirectory);
            if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return;

            foreach (string sourcePath in Directory.EnumerateFiles(sourceDirectory))
            {
                string relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
                if (includeFile(relativePath))
                    CopyFileMissing(sourcePath, Path.Combine(destinationRoot, relativePath));
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(sourceDirectory))
                CopyDirectoryMissing(sourceRoot, childDirectory, destinationRoot, includeFile);
        }

        private static void CopyFileMissing(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath) || File.Exists(destinationPath))
                return;

            string destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
            Directory.CreateDirectory(destinationDirectory);
            string temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".migrating";
            try
            {
                File.Copy(sourcePath, temporaryPath, false);
                try
                {
                    File.Move(temporaryPath, destinationPath, false);
                }
                catch (IOException) when (File.Exists(destinationPath))
                {
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }
}

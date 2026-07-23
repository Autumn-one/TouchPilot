using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace GestureSign.Updater
{
    internal sealed class UpdateInstaller
    {
        internal const string ManifestFileName = "release-manifest.json";
        private const string JournalFileName = "update-transaction.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string _backupRootDirectory;

        public UpdateInstaller(string backupRootDirectory = null)
        {
            _backupRootDirectory = backupRootDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TouchPilot", "UpdateBackups");
        }

        public void Install(string packagePath, string targetDirectory, string expectedVersion,
            string expectedPackageSha256)
        {
            ValidateInstallationInputs(packagePath, targetDirectory, expectedPackageSha256);
            RecoverInterruptedTransactions(targetDirectory);

            string workDirectory = Path.Combine(Path.GetTempPath(), "TouchPilot.Update." +
                Guid.NewGuid().ToString("N"));
            string stagingDirectory = Path.Combine(workDirectory, "staging");
            Directory.CreateDirectory(stagingDirectory);

            try
            {
                ZipFile.ExtractToDirectory(packagePath, stagingDirectory);
                ReleaseManifest manifest = LoadAndValidateManifest(stagingDirectory, expectedVersion);
                IReadOnlyList<ValidatedFile> files = ValidatePackageFiles(stagingDirectory, targetDirectory, manifest);
                ReleaseManifest installedManifest = TryLoadInstalledManifest(targetDirectory);
                IReadOnlyList<string> obsoleteFiles = FindObsoleteFiles(installedManifest, manifest);
                ApplyFiles(files, obsoleteFiles, targetDirectory, manifest.Version);
            }
            finally
            {
                TryDeleteDirectory(workDirectory);
            }
        }

        private static void ValidateInstallationInputs(string packagePath, string targetDirectory,
            string expectedPackageSha256)
        {
            if (!File.Exists(packagePath))
                throw new FileNotFoundException("The downloaded update package was not found.", packagePath);
            if (string.IsNullOrWhiteSpace(expectedPackageSha256) || expectedPackageSha256.Length != 64 ||
                !string.Equals(ComputeSha256(packagePath), expectedPackageSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update package failed SHA-256 verification.");
            if (!Directory.Exists(targetDirectory))
                throw new DirectoryNotFoundException("The TouchPilot installation directory was not found.");
            if (!File.Exists(Path.Combine(targetDirectory, "TouchPilot.exe")) &&
                !File.Exists(Path.Combine(targetDirectory, "GestureSign.exe")))
                throw new InvalidDataException("The target directory is not a TouchPilot installation.");
        }

        private static ReleaseManifest LoadAndValidateManifest(string stagingDirectory, string expectedVersion)
        {
            string manifestPath = Path.Combine(stagingDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
                throw new InvalidDataException("The update package does not contain a release manifest.");

            ReleaseManifest manifest = JsonSerializer.Deserialize<ReleaseManifest>(
                File.ReadAllText(manifestPath), JsonOptions);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version) || manifest.Files == null ||
                manifest.Files.Count == 0)
                throw new InvalidDataException("The release manifest is invalid.");
            if (!string.Equals(manifest.Version, expectedVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded package version does not match the selected release.");

            return manifest;
        }

        private static IReadOnlyList<ValidatedFile> ValidatePackageFiles(string stagingDirectory,
            string targetDirectory, ReleaseManifest manifest)
        {
            var files = new List<ValidatedFile>(manifest.Files.Count + 1);
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ReleaseFileEntry entry in manifest.Files)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path) || string.IsNullOrWhiteSpace(entry.Sha256))
                    throw new InvalidDataException("The release manifest contains an invalid file entry.");
                if (!uniquePaths.Add(entry.Path))
                    throw new InvalidDataException("The release manifest contains duplicate file paths.");

                string sourcePath = GetSafePath(stagingDirectory, entry.Path);
                string targetPath = GetSafePath(targetDirectory, entry.Path);
                if (!File.Exists(sourcePath))
                    throw new InvalidDataException("The update package is missing " + entry.Path + ".");

                var fileInfo = new FileInfo(sourcePath);
                if (fileInfo.Length != entry.Size)
                    throw new InvalidDataException("The size of " + entry.Path + " does not match the release manifest.");

                string actualHash = ComputeSha256(sourcePath);
                if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The SHA-256 hash of " + entry.Path + " is invalid.");

                files.Add(new ValidatedFile(sourcePath, targetPath));
            }

            foreach (string packageFile in Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(stagingDirectory, packageFile)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (!string.Equals(relativePath, ManifestFileName, StringComparison.OrdinalIgnoreCase) &&
                    !uniquePaths.Contains(relativePath))
                    throw new InvalidDataException("The update package contains an unlisted file: " +
                                                   relativePath + ".");
            }

            string manifestTargetPath = Path.Combine(targetDirectory, ManifestFileName);
            files.Add(new ValidatedFile(Path.Combine(stagingDirectory, ManifestFileName),
                manifestTargetPath));
            return files;
        }

        private static ReleaseManifest TryLoadInstalledManifest(string targetDirectory)
        {
            string path = Path.Combine(targetDirectory, ManifestFileName);
            if (!File.Exists(path))
                return null;

            try
            {
                ReleaseManifest manifest = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(path),
                    JsonOptions);
                if (manifest == null || manifest.Files == null)
                    throw new InvalidDataException("The installed release manifest is invalid.");
                return manifest;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The installed release manifest is invalid.", exception);
            }
        }

        private static IReadOnlyList<string> FindObsoleteFiles(ReleaseManifest installed,
            ReleaseManifest replacement)
        {
            if (installed?.Files == null)
                return Array.Empty<string>();

            var replacementPaths = new HashSet<string>(replacement.Files.Select(entry => entry.Path),
                StringComparer.OrdinalIgnoreCase);
            return installed.Files.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Path) &&
                                                  !string.Equals(entry.Path, ManifestFileName,
                                                      StringComparison.OrdinalIgnoreCase) &&
                                                  !replacementPaths.Contains(entry.Path))
                .Select(entry => entry.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void ApplyFiles(IReadOnlyList<ValidatedFile> files, IReadOnlyList<string> obsoleteFiles,
            string targetDirectory, string version)
        {
            string backupDirectory = Path.Combine(_backupRootDirectory,
                version + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" +
                Guid.NewGuid().ToString("N"));
            var journal = new UpdateTransactionJournal
            {
                TargetDirectory = Path.GetFullPath(targetDirectory),
                State = UpdateTransactionState.Prepared
            };
            var appliedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (ValidatedFile file in files)
                {
                    string relativePath = Path.GetRelativePath(targetDirectory, file.TargetPath);
                    PrepareJournalEntry(journal, backupDirectory, targetDirectory, relativePath);
                }

                foreach (string obsoleteFile in obsoleteFiles)
                    PrepareJournalEntry(journal, backupDirectory, targetDirectory, obsoleteFile);

                SaveJournal(backupDirectory, journal);

                foreach (ValidatedFile file in files)
                {
                    ReplaceFile(file.SourcePath, file.TargetPath);
                    appliedPaths.Add(Path.GetRelativePath(targetDirectory, file.TargetPath)
                        .Replace(Path.DirectorySeparatorChar, '/'));
                }
                foreach (string obsoleteFile in obsoleteFiles)
                {
                    string targetPath = GetSafePath(targetDirectory, obsoleteFile);
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                        appliedPaths.Add(obsoleteFile.Replace(Path.DirectorySeparatorChar, '/'));
                    }
                }

                journal.State = UpdateTransactionState.Completed;
                SaveJournal(backupDirectory, journal);
            }
            catch (Exception updateException)
            {
                try
                {
                    Rollback(journal, backupDirectory, appliedPaths);
                    journal.State = UpdateTransactionState.RolledBack;
                    SaveJournal(backupDirectory, journal);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException("The update failed and could not be rolled back completely.",
                        updateException, rollbackException);
                }

                throw;
            }
        }

        private void RecoverInterruptedTransactions(string targetDirectory)
        {
            if (!Directory.Exists(_backupRootDirectory))
                return;

            foreach (string backupDirectory in Directory.EnumerateDirectories(_backupRootDirectory))
            {
                try
                {
                    string journalPath = Path.Combine(backupDirectory, JournalFileName);
                    if (!File.Exists(journalPath))
                        continue;
                    UpdateTransactionJournal journal = JsonSerializer.Deserialize<UpdateTransactionJournal>(
                        File.ReadAllText(journalPath), JsonOptions);
                    if (journal == null || journal.State != UpdateTransactionState.Prepared ||
                        !string.Equals(Path.GetFullPath(journal.TargetDirectory), Path.GetFullPath(targetDirectory),
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    Rollback(journal, backupDirectory, null);
                    journal.State = UpdateTransactionState.RolledBack;
                    SaveJournal(backupDirectory, journal);
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException("An interrupted update could not be recovered safely.",
                        exception);
                }
            }
        }

        private static void PrepareJournalEntry(UpdateTransactionJournal journal, string backupDirectory,
            string targetDirectory, string relativePath)
        {
            if (journal.Entries.Any(entry => string.Equals(entry.Path, relativePath,
                    StringComparison.OrdinalIgnoreCase)))
                return;

            string targetPath = GetSafePath(targetDirectory, relativePath);
            string backupPath = GetSafePath(backupDirectory, relativePath);
            bool hadOriginal = File.Exists(targetPath);
            if (hadOriginal)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                File.Copy(targetPath, backupPath, true);
            }

            journal.Entries.Add(new UpdateTransactionEntry
            {
                Path = relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                HadOriginal = hadOriginal
            });
        }

        private static void ReplaceFile(string sourcePath, string targetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            string temporaryTarget = targetPath + ".update-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(sourcePath, temporaryTarget, true);
                File.Move(temporaryTarget, targetPath, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryTarget))
                        File.Delete(temporaryTarget);
                }
                catch
                {
                }
            }
        }

        private static void Rollback(UpdateTransactionJournal journal, string backupDirectory,
            ISet<string> appliedPaths)
        {
            var failures = new List<Exception>();
            foreach (UpdateTransactionEntry entry in journal.Entries.AsEnumerable().Reverse())
            {
                if (appliedPaths != null && !appliedPaths.Contains(entry.Path))
                    continue;

                try
                {
                    string targetPath = GetSafePath(journal.TargetDirectory, entry.Path);
                    if (entry.HadOriginal)
                    {
                        string backupPath = GetSafePath(backupDirectory, entry.Path);
                        if (!File.Exists(backupPath))
                            throw new FileNotFoundException("An update backup file is missing.", backupPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                        File.Copy(backupPath, targetPath, true);
                    }
                    else if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count > 0)
                throw new AggregateException("One or more update files could not be restored.", failures);
        }

        private static void SaveJournal(string backupDirectory, UpdateTransactionJournal journal)
        {
            Directory.CreateDirectory(backupDirectory);
            string journalPath = Path.Combine(backupDirectory, JournalFileName);
            string temporaryPath = journalPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(journal, JsonOptions));
            File.Move(temporaryPath, journalPath, true);
        }

        private static string GetSafePath(string rootDirectory, string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
                throw new InvalidDataException("The release manifest contains an absolute path.");

            string root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The release manifest contains a path outside the installation directory.");
            return fullPath;
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private sealed class ReleaseManifest
        {
            public string Version { get; set; }
            public List<ReleaseFileEntry> Files { get; set; }
        }

        private sealed class ReleaseFileEntry
        {
            public string Path { get; set; }
            public string Sha256 { get; set; }
            public long Size { get; set; }
        }

        private sealed class ValidatedFile
        {
            public ValidatedFile(string sourcePath, string targetPath)
            {
                SourcePath = sourcePath;
                TargetPath = targetPath;
            }

            public string SourcePath { get; }
            public string TargetPath { get; }
        }

        private sealed class UpdateTransactionJournal
        {
            public string TargetDirectory { get; set; }
            public string State { get; set; }
            public List<UpdateTransactionEntry> Entries { get; set; } = new List<UpdateTransactionEntry>();
        }

        private sealed class UpdateTransactionEntry
        {
            public string Path { get; set; }
            public bool HadOriginal { get; set; }
        }

        private static class UpdateTransactionState
        {
            public const string Prepared = "prepared";
            public const string Completed = "completed";
            public const string RolledBack = "rolledBack";
        }
    }
}

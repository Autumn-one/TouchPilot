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

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string _backupRootDirectory;

        public UpdateInstaller(string backupRootDirectory = null)
        {
            _backupRootDirectory = backupRootDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GestureSign", "UpdateBackups");
        }

        public void Install(string packagePath, string targetDirectory, string expectedVersion,
            string expectedPackageSha256)
        {
            ValidateInstallationInputs(packagePath, targetDirectory, expectedPackageSha256);

            string workDirectory = Path.Combine(Path.GetTempPath(), "GestureSign.Update." + Guid.NewGuid().ToString("N"));
            string stagingDirectory = Path.Combine(workDirectory, "staging");
            Directory.CreateDirectory(stagingDirectory);

            try
            {
                ZipFile.ExtractToDirectory(packagePath, stagingDirectory);
                ReleaseManifest manifest = LoadAndValidateManifest(stagingDirectory, expectedVersion);
                IReadOnlyList<ValidatedFile> files = ValidatePackageFiles(stagingDirectory, targetDirectory, manifest);
                ApplyFiles(files, targetDirectory, manifest.Version);
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
                throw new DirectoryNotFoundException("The GestureSign installation directory was not found.");
            if (!File.Exists(Path.Combine(targetDirectory, "GestureSign.exe")))
                throw new InvalidDataException("The target directory is not a GestureSign installation.");
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

            string manifestTargetPath = Path.Combine(targetDirectory, ManifestFileName);
            files.Add(new ValidatedFile(Path.Combine(stagingDirectory, ManifestFileName),
                manifestTargetPath));
            return files;
        }

        private void ApplyFiles(IReadOnlyList<ValidatedFile> files, string targetDirectory, string version)
        {
            string backupDirectory = Path.Combine(_backupRootDirectory,
                version + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            var appliedFiles = new List<AppliedFile>(files.Count);

            try
            {
                foreach (ValidatedFile file in files)
                {
                    string relativePath = Path.GetRelativePath(targetDirectory, file.TargetPath);
                    string backupPath = GetSafePath(backupDirectory, relativePath);
                    bool hadOriginal = File.Exists(file.TargetPath);

                    if (hadOriginal)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                        File.Copy(file.TargetPath, backupPath, true);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath));
                    string temporaryTarget = file.TargetPath + ".update-" + Guid.NewGuid().ToString("N");
                    File.Copy(file.SourcePath, temporaryTarget, true);
                    File.Move(temporaryTarget, file.TargetPath, true);
                    appliedFiles.Add(new AppliedFile(file.TargetPath, backupPath, hadOriginal));
                }
            }
            catch
            {
                Rollback(appliedFiles);
                throw;
            }
        }

        private static void Rollback(IEnumerable<AppliedFile> appliedFiles)
        {
            foreach (AppliedFile file in appliedFiles.Reverse())
            {
                try
                {
                    if (file.HadOriginal)
                        File.Copy(file.BackupPath, file.TargetPath, true);
                    else if (File.Exists(file.TargetPath))
                        File.Delete(file.TargetPath);
                }
                catch
                {
                }
            }
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

        private sealed class AppliedFile
        {
            public AppliedFile(string targetPath, string backupPath, bool hadOriginal)
            {
                TargetPath = targetPath;
                BackupPath = backupPath;
                HadOriginal = hadOriginal;
            }

            public string TargetPath { get; }
            public string BackupPath { get; }
            public bool HadOriginal { get; }
        }
    }
}

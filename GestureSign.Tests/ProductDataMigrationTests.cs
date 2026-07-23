using GestureSign.Common.Configuration;
using System;
using System.IO;
using Xunit;

namespace GestureSign.Tests
{
    public class ProductDataMigrationTests
    {
        [Fact]
        public void InstalledMigrationCopiesUserDataAndRenamesConfigWithoutMovingCaches()
        {
            using var directory = new TemporaryDirectory();
            string legacyRoaming = directory.CreateDirectory("legacy-roaming");
            string currentRoaming = directory.CreateDirectory("current-roaming");
            string legacyLocal = directory.CreateDirectory("legacy-local");
            string currentLocal = directory.CreateDirectory("current-local");
            File.WriteAllText(Path.Combine(legacyRoaming, "Actions.gsa"), "actions");
            File.WriteAllText(Path.Combine(legacyRoaming, "Gestures.gest"), "gestures");
            File.WriteAllText(Path.Combine(legacyRoaming, ProductDataMigration.LegacyConfigFileName),
                "legacy-config");
            Directory.CreateDirectory(Path.Combine(legacyLocal, "Backup"));
            File.WriteAllText(Path.Combine(legacyLocal, "Backup", "settings.gsb"), "backup");
            Directory.CreateDirectory(Path.Combine(legacyLocal, "Updates"));
            File.WriteAllText(Path.Combine(legacyLocal, "Updates", "stale.zip"), "stale");
            File.WriteAllText(Path.Combine(legacyLocal, "GestureSign.log"), "old-log");

            ProductDataMigration.MigrateInstalledData(legacyRoaming, currentRoaming,
                legacyLocal, currentLocal);

            Assert.Equal("actions", File.ReadAllText(Path.Combine(currentRoaming, "Actions.gsa")));
            Assert.Equal("gestures", File.ReadAllText(Path.Combine(currentRoaming, "Gestures.gest")));
            Assert.Equal("legacy-config", File.ReadAllText(Path.Combine(currentRoaming,
                ProductDataMigration.CurrentConfigFileName)));
            Assert.Equal("backup", File.ReadAllText(Path.Combine(currentLocal, "Backup", "settings.gsb")));
            Assert.False(Directory.Exists(Path.Combine(currentLocal, "Updates")));
            Assert.False(File.Exists(Path.Combine(currentLocal, "GestureSign.log")));
            Assert.True(File.Exists(Path.Combine(legacyRoaming, "Actions.gsa")));
        }

        [Fact]
        public void MigrationNeverOverwritesExistingTouchPilotData()
        {
            using var directory = new TemporaryDirectory();
            string legacyRoaming = directory.CreateDirectory("legacy-roaming");
            string currentRoaming = directory.CreateDirectory("current-roaming");
            string legacyLocal = directory.CreateDirectory("legacy-local");
            string currentLocal = directory.CreateDirectory("current-local");
            File.WriteAllText(Path.Combine(legacyRoaming, "Actions.gsa"), "legacy-actions");
            File.WriteAllText(Path.Combine(currentRoaming, "Actions.gsa"), "current-actions");
            File.WriteAllText(Path.Combine(legacyRoaming, ProductDataMigration.LegacyConfigFileName),
                "legacy-config");
            File.WriteAllText(Path.Combine(currentRoaming, ProductDataMigration.CurrentConfigFileName),
                "current-config");

            ProductDataMigration.MigrateInstalledData(legacyRoaming, currentRoaming,
                legacyLocal, currentLocal);

            Assert.Equal("current-actions", File.ReadAllText(Path.Combine(currentRoaming, "Actions.gsa")));
            Assert.Equal("current-config", File.ReadAllText(Path.Combine(currentRoaming,
                ProductDataMigration.CurrentConfigFileName)));
        }

        [Fact]
        public void PortableMigrationRenamesConfigInPlace()
        {
            using var directory = new TemporaryDirectory();
            File.WriteAllText(Path.Combine(directory.Path, ProductDataMigration.LegacyConfigFileName),
                "portable-config");

            ProductDataMigration.MigratePortableData(directory.Path);

            Assert.Equal("portable-config", File.ReadAllText(Path.Combine(directory.Path,
                ProductDataMigration.CurrentConfigFileName)));
            Assert.True(File.Exists(Path.Combine(directory.Path,
                ProductDataMigration.LegacyConfigFileName)));
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "TouchPilot.DataMigrationTests." + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public string CreateDirectory(string name)
            {
                string path = System.IO.Path.Combine(Path, name);
                Directory.CreateDirectory(path);
                return path;
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, true);
                }
                catch
                {
                }
            }
        }
    }
}

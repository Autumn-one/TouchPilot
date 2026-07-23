using GestureSign.Common;
using GestureSign.ControlPanel.Common;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace GestureSign.Tests
{
    public class ProductIdentityTests
    {
        [Fact]
        public void RuntimeIdentityUsesTouchPilotNames()
        {
            Assert.Equal("TouchPilot", Constants.ProductName);
            Assert.Equal("TouchPilot.exe", Constants.DaemonFileName);
            Assert.Equal("TouchPilot.ControlPanel.exe", Constants.ControlPanelFileName);
            Assert.Equal("TouchPilot.Updater.exe", Constants.UpdaterFileName);
            Assert.Equal("Autumn-one/TouchPilot", Constants.DefaultGitHubRepository);
            Assert.Equal("TouchPilot", typeof(GestureSign.Daemon.Program).Assembly.GetName().Name);
            Assert.Equal("TouchPilot.ControlPanel",
                typeof(GestureSign.ControlPanel.App).Assembly.GetName().Name);
            Assert.Equal("TouchPilot.Updater",
                typeof(GestureSign.Updater.Program).Assembly.GetName().Name);
            Assert.Equal("TouchPilot.ReleaseManager",
                typeof(GestureSign.ReleaseManager.App).Assembly.GetName().Name);
        }

        [Fact]
        public void ElevatedStartupTaskUsesTouchPilotIdentityAndEscapesExecutablePath()
        {
            string executablePath = @"C:\Program Files\Touch & Pilot\TouchPilot.exe";
            var generated = StartupHelper.CreateStartupTaskDocument(executablePath);
            var document = XDocument.Parse(generated.ToString());
            var taskNamespace = document.Root.Name.Namespace;

            Assert.Equal("TouchPilot",
                document.Descendants(taskNamespace + "Author").Single().Value);
            Assert.Equal("Run TouchPilot on startup with elevated privileges.",
                document.Descendants(taskNamespace + "Description").Single().Value);
            Assert.Equal(executablePath,
                document.Descendants(taskNamespace + "Command").Single().Value);
            Assert.Equal("TouchPilot Startup", StartupHelper.CurrentTaskName);
            Assert.Equal("StartGestureSign", StartupHelper.LegacyTaskName);
        }

        [Fact]
        public void LegacyStartupMigrationOnlyAcceptsCurrentInstallationTargets()
        {
            string directory = Path.Combine(Path.GetTempPath(), "TouchPilot", "Current");

            Assert.True(StartupHelper.IsStartupTargetForDirectory(
                Path.Combine(directory, "GestureSign.exe"), directory));
            Assert.True(StartupHelper.IsStartupTargetForDirectory(
                Path.Combine(directory, "TouchPilot.exe"), directory));
            Assert.False(StartupHelper.IsStartupTargetForDirectory(
                Path.Combine(Path.GetTempPath(), "Other", "GestureSign.exe"), directory));
            Assert.False(StartupHelper.IsStartupTargetForDirectory(null, directory));
        }
    }
}

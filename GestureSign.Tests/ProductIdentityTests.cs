using GestureSign.Common;
using GestureSign.ControlPanel.Common;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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

        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(false, true, true, false)]
        [InlineData(true, false, false, false)]
        [InlineData(true, true, false, true)]
        [InlineData(true, false, true, true)]
        public void ElevatedStartupRequiresConfigurationAndARegisteredTask(
            bool configured, bool currentTaskRegistered, bool legacyTaskRegistered,
            bool expected)
        {
            Assert.Equal(expected, StartupHelper.ShouldUseHighPrivilegeStartup(configured,
                currentTaskRegistered, legacyTaskRegistered));
        }

        [Fact]
        public void ElevatedStartupScriptsTreatMissingLegacyTasksAsAlreadyClean()
        {
            string uniqueSuffix = Guid.NewGuid().ToString("N");
            string script = StartupHelper.BuildDeleteTasksScript(
                "TouchPilot Missing Current " + uniqueSuffix,
                "TouchPilot Missing Legacy " + uniqueSuffix);

            Assert.Contains("$ErrorActionPreference='SilentlyContinue'",
                StartupHelper.BuildCreateTaskScript(Path.Combine(Path.GetTempPath(),
                    "TouchPilot.StartupTask.xml")));
            Assert.Equal(0, RunWindowsPowerShell(script));
        }

        private static int RunWindowsPowerShell(string script)
        {
            string powerShellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            var startInfo = new ProcessStartInfo(powerShellPath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-WindowStyle");
            startInfo.ArgumentList.Add("Hidden");
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(Convert.ToBase64String(
                Encoding.Unicode.GetBytes(script)));

            using Process process = Process.Start(startInfo);
            Assert.NotNull(process);
            Assert.True(process.WaitForExit(30000),
                "The startup cleanup regression script did not exit in time.");
            return process.ExitCode;
        }
    }
}

using GestureSign.Common.Lifecycle;
using GestureSign.ControlPanel.Common;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace GestureSign.Tests
{
    public class DaemonStartupTests
    {
        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(true, false, false, true)]
        [InlineData(true, true, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(false, true, false, false)]
        public void ElevationHonorsPreferenceWithoutRelaunchingPrivilegedBuilds(
            bool configured, bool elevated, bool uiAccess, bool expected)
        {
            Assert.Equal(expected, DaemonStartup.NeedsElevation(configured, elevated, uiAccess));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LaunchUsesCorrectWorkingDirectoryAndEnablesShellForRunAs(bool elevate)
        {
            string path = @"C:\Touch Pilot & tools\TouchPilot.exe";
            var info = DaemonStartup.CreateStartInfo(path, elevate);
            Assert.Equal(path, info.FileName);
            Assert.Equal(Path.GetDirectoryName(path), info.WorkingDirectory);
            Assert.Equal(elevate, info.UseShellExecute);
            Assert.Equal(elevate ? "runas" : string.Empty, info.Verb);
            Assert.Empty(info.ArgumentList);
        }

        [Theory]
        [InlineData("TouchPilot.exe")]
        [InlineData("GestureSign.exe")]
        public void MatchingElevatedTaskCanBeReused(string executableName)
        {
            XDocument document = CreateTask(executableName);
            Assert.True(StartupHelper.CanRunElevatedStartupTask(document, @"C:\Touch Pilot", "S-1-5-21-123"));
        }

        [Theory]
        [InlineData("other-installation")]
        [InlineData("other-user")]
        [InlineData("normal-permissions")]
        [InlineData("noninteractive")]
        [InlineData("arguments")]
        [InlineData("extra-action")]
        public void TaskReuseRejectsUnrelatedOrNonElevatedLaunches(string change)
        {
            XDocument document = CreateTask("TouchPilot.exe");
            XNamespace ns = document.Root.Name.Namespace;
            XElement principal = document.Descendants(ns + "Principal").Single();
            XElement action = document.Descendants(ns + "Exec").Single();
            switch (change)
            {
                case "other-installation": action.Element(ns + "Command").Value = @"C:\Other\TouchPilot.exe"; break;
                case "other-user": principal.Element(ns + "UserId").Value = "S-1-5-21-456"; break;
                case "normal-permissions": principal.Element(ns + "RunLevel").Value = "LeastPrivilege"; break;
                case "noninteractive": principal.Element(ns + "LogonType").Value = "Password"; break;
                case "arguments": action.Add(new XElement(ns + "Arguments", "--unexpected")); break;
                case "extra-action": action.Parent.Add(new XElement(action)); break;
            }
            Assert.False(StartupHelper.CanRunElevatedStartupTask(document, @"C:\Touch Pilot", "S-1-5-21-123"));
        }

        [Fact]
        public void MissingTaskFallsBackToWindowsElevation()
        {
            Assert.False(StartupHelper.CanRunElevatedStartupTask(null, @"C:\Touch Pilot", "S-1-5-21-123"));
        }

        [Fact]
        public async Task RuntimeInspectionChecksProcessTokenAndIgnoresOtherInstallation()
        {
            string directory = Path.Combine(Path.GetTempPath(), "TouchPilot-startup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Process process = null;
            try
            {
                const string host = "GestureSign.Tests.DesktopHost";
                foreach (string extension in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
                    File.Copy(Path.Combine(AppContext.BaseDirectory, host + extension), Path.Combine(directory, host + extension));
                string executable = Path.Combine(directory, "TouchPilot.exe");
                File.Copy(Path.Combine(AppContext.BaseDirectory, host + ".exe"), executable);
                var info = DaemonStartup.CreateStartInfo(executable, false);
                info.ArgumentList.Add("--startup-probe");
                info.RedirectStandardInput = true;
                info.RedirectStandardOutput = true;
                info.CreateNoWindow = true;
                process = Process.Start(info);
                string ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(process.Id.ToString(), ready);
                Assert.True(DaemonStartup.TryGetRunningElevation(directory, out bool elevated));
                Assert.Equal(DaemonStartup.IsCurrentProcessElevated, elevated);
                Assert.False(DaemonStartup.TryGetRunningElevation(Path.Combine(directory, "other"), out _));
            }
            finally
            {
                if (process != null)
                {
                    process.StandardInput.Close();
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    process.Dispose();
                }
                Directory.Delete(directory, true);
            }
        }

        private static XDocument CreateTask(string executableName)
        {
            XDocument document = StartupHelper.CreateStartupTaskDocument(Path.Combine(@"C:\Touch Pilot", executableName));
            XNamespace ns = document.Root.Name.Namespace;
            document.Descendants(ns + "Principal").Single().Add(new XElement(ns + "UserId", "S-1-5-21-123"));
            return document;
        }

        [Fact]
        public void TaskSchedulerAccountNamesResolveToTheCurrentUserSid()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            XDocument document = CreateTask("TouchPilot.exe");
            XNamespace ns = document.Root.Name.Namespace;
            document.Descendants(ns + "UserId").Single().Value = identity.Name;
            Assert.True(StartupHelper.CanRunElevatedStartupTask(document, @"C:\Touch Pilot", identity.User.Value));
        }
    }
}

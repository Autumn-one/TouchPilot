using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using GestureSign.Common.Plugins;
using GestureSign.CorePlugins.OpenFile;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GestureSign.Tests
{
    [Collection(DesktopInputIntegrationCollection.Name)]
    public class OpenFilePermissionTests
    {
        private static string ProbePath => Path.Combine(AppContext.BaseDirectory, "GestureSign.Tests.DesktopHost.exe");

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("null")]
        [InlineData("{\"Path\":\"example.txt\",\"Variables\":\"two words\"}")]
        public void ExistingSettingsDefaultToNormalPermissions(string json)
        {
            var plugin = new OpenFilePlugin();
            Assert.True(plugin.Deserialize(json));
            var settings = JObject.Parse(plugin.Serialize());
            Assert.False(settings.Value<bool>("RunAsAdministrator"));
            if (json?.Contains("example.txt") == true)
            {
                Assert.Equal("example.txt", settings.Value<string>("Path"));
                Assert.Equal("two words", settings.Value<string>("Variables"));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PermissionsRoundTrip(bool administrator)
        {
            var plugin = new OpenFilePlugin();
            Assert.True(plugin.Deserialize(JsonConvert.SerializeObject(new
            {
                Path = "https://example.com/?q=a&b=c",
                Variables = "\"two words\"",
                RunAsAdministrator = administrator
            })));
            var restored = new OpenFilePlugin();
            Assert.True(restored.Deserialize(plugin.Serialize()));
            Assert.Equal(plugin.Serialize(), restored.Serialize());
            Assert.Equal(administrator, JObject.Parse(restored.Serialize()).Value<bool>("RunAsAdministrator"));
        }

        [Fact]
        public void NormalLaunchPreservesRelativePathArgumentsAndGestureVariables()
        {
            using var output = new ProbeDirectory();
            string report = Path.Combine(output.Path, "normal.json");
            var plugin = new OpenFilePlugin();
            Assert.True(plugin.Deserialize(JsonConvert.SerializeObject(new
            {
                Path = Path.GetRelativePath(Environment.CurrentDirectory, ProbePath),
                Variables = $"--open-file-report \"{report}\" \"two words\" \"a&b\" %GS_StartPoint_X%"
            })));
            var points = new List<Point> { new Point(17, 29) };
            Assert.True(plugin.Gestured(new PointInfo(points, new List<List<Point>> { points }, null,
                new SynchronizationContext())));
            AssertReport(ReadReport(report), false, Environment.CurrentDirectory);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DesktopShellLaunchPreservesArgumentsWorkingDirectoryAndShortcuts(bool shortcut)
        {
            using var output = new ProbeDirectory();
            string report = Path.Combine(output.Path, "shell.json");
            string target = ProbePath;
            string arguments = $"--open-file-report \"{report}\" \"two words\" \"a&b\" 17";
            if (shortcut)
            {
                target = Path.Combine(output.Path, "probe shortcut.lnk");
                CreateShortcut(target, arguments, output.Path);
                target = Path.GetFileName(target);
                arguments = null;
            }
            // xUnit runs this on MTA, matching the gesture command worker.
            Assert.Equal(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState());
            OpenFileLauncher.OpenWithDesktopShell(target, arguments, output.Path);
            AssertReport(ReadReport(report), false, output.Path);
        }

        [Fact]
        public void InvalidLaunchReportsFailure()
        {
            Assert.Throws<ArgumentException>(() => OpenFileLauncher.Open("", null, false));
            // The direct ShellExecute path must retain its original missing-file error.
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                Assert.Throws<Win32Exception>(() => OpenFileLauncher.Open(
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"), null, false));
        }

        [ElevationFact]
        public void ElevationMatrixUsesTheRequestedPermissionsAcrossProcessBoundaries()
        {
            using var output = new ProbeDirectory();
            Assert.True(OpenFileLauncher.Open(ProbePath, $"--open-file-matrix \"{output.Path}\"", true),
                "The Windows UAC prompt was cancelled.");
            Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(output.Path, "administrator.json")) ||
                File.Exists(Path.Combine(output.Path, "error.txt")), TimeSpan.FromSeconds(75)),
                "The elevated permission matrix timed out.");
            string error = Path.Combine(output.Path, "error.txt");
            Assert.False(File.Exists(error), File.Exists(error) ? File.ReadAllText(error) : string.Empty);
            Assert.True(ReadReport(Path.Combine(output.Path, "parent.json")).Value<bool>("IsAdministrator"));
            AssertReport(ReadReport(Path.Combine(output.Path, "normal.json")), false, Environment.CurrentDirectory);
            AssertReport(ReadReport(Path.Combine(output.Path, "administrator.json")), true, Environment.CurrentDirectory);
        }

        private static JObject ReadReport(string path)
        {
            Assert.True(SpinWait.SpinUntil(() => File.Exists(path), TimeSpan.FromSeconds(15)),
                "The launched process did not write its permission report.");
            return JObject.Parse(File.ReadAllText(path));
        }

        private static void AssertReport(JObject report, bool administrator, string directory)
        {
            Assert.Equal(administrator, report.Value<bool>("IsAdministrator"));
            Assert.Equal(directory, report.Value<string>("WorkingDirectory"), ignoreCase: true);
            Assert.Equal(new[] { "two words", "a&b", "17" }, report["Arguments"].Values<string>().ToArray());
            if (!administrator)
            {
                using var identity = WindowsIdentity.GetCurrent();
                Assert.Equal(identity.User.Value, report.Value<string>("UserSid"));
            }
        }

        private static void CreateShortcut(string path, string arguments, string workingDirectory)
        {
            object script = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            object shortcut = null;
            try
            {
                shortcut = ((dynamic)script).CreateShortcut(path);
                ((dynamic)shortcut).TargetPath = ProbePath;
                ((dynamic)shortcut).Arguments = arguments;
                ((dynamic)shortcut).WorkingDirectory = workingDirectory;
                ((dynamic)shortcut).Save();
            }
            finally
            {
                if (shortcut != null) Marshal.ReleaseComObject(shortcut);
                Marshal.ReleaseComObject(script);
            }
        }

        private sealed class ProbeDirectory : IDisposable
        {
            internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "TouchPilot permission \u6d4b\u8bd5 " + Guid.NewGuid().ToString("N"));

            internal ProbeDirectory() => Directory.CreateDirectory(Path);

            public void Dispose() => Directory.Delete(Path, recursive: true);
        }

        public sealed class ElevationFactAttribute : FactAttribute
        {
            public ElevationFactAttribute()
            {
                if (Environment.GetEnvironmentVariable("TOUCHPILOT_TEST_ELEVATION") != "1")
                    Skip = "Set TOUCHPILOT_TEST_ELEVATION=1 to run the real Windows UAC permission matrix.";
            }
        }
    }
}

using GestureSign.Common.Configuration;
using GestureSign.Common.Localization;
using GestureSign.Common.Lifecycle;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using System.Xml.Linq;
using File = System.IO.File;

namespace GestureSign.ControlPanel.Common
{
    static class StartupHelper
    {
        internal const string CurrentTaskName = "TouchPilot Startup";
        internal const string LegacyTaskName = "StartGestureSign";

        private static string DaemonPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, GestureSign.Common.Constants.DaemonFileName);

        private static string StartupLnkPath => Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\" + GestureSign.Common.Constants.ProductName + ".lnk";

        private static string LegacyStartupLnkPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), "GestureSign.lnk");

        public static bool IsRunAsAdmin => AppConfig.RunAsAdmin;

        private static void CreateLnk(string lnkPath, string targetPath)
        {
            ShortcutHelper.Create(lnkPath, targetPath, Application.ResourceAssembly.GetName().Version.ToString());
        }

        private static bool AddStartupTask(string filePath)
        {
            string xmlFilePath = Path.Combine(AppConfig.LocalApplicationDataPath,
                "TouchPilot.StartupTask.xml");
            try
            {
                XDocument taskDocument = CreateStartupTaskDocument(filePath);
                var settings = new XmlWriterSettings
                {
                    Encoding = Encoding.Unicode,
                    Indent = true
                };
                using (XmlWriter writer = XmlWriter.Create(xmlFilePath, settings))
                {
                    taskDocument.Save(writer);
                }

                return RunElevatedPowerShell(BuildCreateTaskScript(xmlFilePath));
            }
            catch (Exception exception)
            {
                GestureSign.Common.Log.Logging.LogAndNotice(exception);
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(xmlFilePath))
                        File.Delete(xmlFilePath);
                }
                catch (Exception exception)
                {
                    GestureSign.Common.Log.Logging.LogException(exception);
                }
            }
        }

        private static bool DelStartupTask()
        {
            try
            {
                return RunElevatedPowerShell(BuildDeleteTasksScript());
            }
            catch (Exception exception)
            {
                GestureSign.Common.Log.Logging.LogAndNotice(exception);
                return false;
            }
        }

        internal static XDocument CreateStartupTaskDocument(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A startup executable path is required.", nameof(filePath));

            XDocument document = XDocument.Parse(Properties.Resources.StartGestureSignTask,
                LoadOptions.PreserveWhitespace);
            XNamespace taskNamespace = document.Root?.Name.Namespace ??
                                       throw new InvalidDataException("The startup task template is invalid.");
            document.Descendants(taskNamespace + "Author").Single().Value =
                GestureSign.Common.Constants.ProductName;
            document.Descendants(taskNamespace + "Description").Single().Value =
                "Run TouchPilot on startup with elevated privileges.";
            document.Descendants(taskNamespace + "Command").Single().Value = filePath;
            return document;
        }

        internal static bool IsStartupTargetForDirectory(string targetPath, string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(directoryPath))
                return false;

            try
            {
                string target = Path.GetFullPath(targetPath);
                string directory = Path.GetFullPath(directoryPath);
                return string.Equals(target, Path.Combine(directory, "GestureSign.exe"),
                           StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(target, Path.Combine(directory,
                               GestureSign.Common.Constants.DaemonFileName),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is ArgumentException ||
                                              exception is NotSupportedException ||
                                              exception is PathTooLongException)
            {
                return false;
            }
        }

        private static bool TryDeleteOwnedLegacyStartupLink()
        {
            if (!File.Exists(LegacyStartupLnkPath))
                return false;

            string targetPath;
            try
            {
                targetPath = ShortcutHelper.GetTargetPath(LegacyStartupLnkPath);
            }
            catch (Exception exception)
            {
                GestureSign.Common.Log.Logging.LogException(exception);
                return false;
            }
            if (!IsStartupTargetForDirectory(targetPath, AppDomain.CurrentDomain.BaseDirectory))
                return false;

            File.Delete(LegacyStartupLnkPath);
            return true;
        }

        private static bool TryMigrateLegacyStartupLink()
        {
            if (!File.Exists(LegacyStartupLnkPath))
                return false;

            string targetPath = ShortcutHelper.GetTargetPath(LegacyStartupLnkPath);
            if (!IsStartupTargetForDirectory(targetPath, AppDomain.CurrentDomain.BaseDirectory))
                return false;

            CreateLnk(StartupLnkPath, DaemonPath);
            try
            {
                File.Delete(LegacyStartupLnkPath);
                return true;
            }
            catch
            {
                File.Delete(StartupLnkPath);
                throw;
            }
        }

        internal static string BuildCreateTaskScript(string xmlFilePath)
        {
            string scheduler = DecodePowerShellValue(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"));
            string taskXml = DecodePowerShellValue(xmlFilePath);
            string currentTask = DecodePowerShellValue(CurrentTaskName);
            string legacyTask = DecodePowerShellValue(LegacyTaskName);
            return "$ErrorActionPreference='SilentlyContinue';" +
                   "$scheduler=" + scheduler + ";$taskXml=" + taskXml + ";" +
                   "$currentTask=" + currentTask + ";$legacyTask=" + legacyTask + ";" +
                   "& $scheduler /Create /TN $currentTask /F /XML $taskXml | Out-Null;" +
                   "if($LASTEXITCODE -ne 0){exit $LASTEXITCODE};" +
                   "& $scheduler /Delete /TN $legacyTask /F 2>$null | Out-Null;" +
                   "& $scheduler /Query /TN $legacyTask 2>$null | Out-Null;" +
                   "if($LASTEXITCODE -eq 0){" +
                   "& $scheduler /Delete /TN $currentTask /F 2>$null | Out-Null;exit 1};exit 0";
        }

        private static string BuildDeleteTasksScript()
        {
            return BuildDeleteTasksScript(CurrentTaskName, LegacyTaskName);
        }

        internal static string BuildDeleteTasksScript(string currentTaskName,
            string legacyTaskName)
        {
            string scheduler = DecodePowerShellValue(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"));
            string currentTask = DecodePowerShellValue(currentTaskName);
            string legacyTask = DecodePowerShellValue(legacyTaskName);
            return "$ErrorActionPreference='SilentlyContinue';$scheduler=" + scheduler + ";" +
                   "$tasks=@(" + currentTask + "," + legacyTask + ");$failed=$false;" +
                   "foreach($task in $tasks){& $scheduler /Delete /TN $task /F 2>$null | Out-Null;" +
                   "& $scheduler /Query /TN $task 2>$null | Out-Null;" +
                   "if($LASTEXITCODE -eq 0){$failed=$true}};if($failed){exit 1};exit 0";
        }

        private static string DecodePowerShellValue(string value)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(value));
            return "[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" +
                   encoded + "'))";
        }

        private static bool RunElevatedPowerShell(string script)
        {
            string powerShellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo(powerShellPath)
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true,
                Verb = "runas"
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-WindowStyle");
            startInfo.ArgumentList.Add("Hidden");
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(encodedScript);

            using Process process = Process.Start(startInfo);
            if (process == null)
                return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }

        private static bool TaskExists(string taskName)
        {
            string schedulerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
            var startInfo = new ProcessStartInfo(schedulerPath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("/Query");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add(taskName);

            using Process process = Process.Start(startInfo);
            if (process == null)
                return false;
            if (!process.WaitForExit(5000))
            {
                process.Kill(true);
                return false;
            }
            return process.ExitCode == 0;
        }

        private static XDocument ReadStartupTask(string taskName)
        {
            string schedulerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
            var startInfo = new ProcessStartInfo(schedulerPath)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("/Query");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add(taskName);
            startInfo.ArgumentList.Add("/XML");

            try
            {
                using Process process = Process.Start(startInfo);
                if (process == null)
                    return null;
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000))
                {
                    process.Kill(true);
                    return null;
                }
                Task.WaitAll(output, error);
                if (process.ExitCode != 0)
                    return null;

                return XDocument.Parse(output.Result);
            }
            catch (Exception exception)
            {
                GestureSign.Common.Log.Logging.LogException(exception);
                return null;
            }
        }

        private static bool IsStartupTaskForCurrentInstallation(string taskName)
        {
            XDocument document = ReadStartupTask(taskName);
            if (document == null)
                return false;
            XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
            return document.Descendants(ns + "Command").Any(command =>
                IsStartupTargetForDirectory(command.Value, AppDomain.CurrentDomain.BaseDirectory));
        }

        internal static bool CanRunElevatedStartupTask(XDocument document, string directory, string userSid)
        {
            XElement root = document?.Root;
            if (root == null)
                return false;
            XNamespace ns = root.Name.Namespace;
            XElement[] principals = root.Element(ns + "Principals")?.Elements().ToArray();
            XElement[] actions = root.Element(ns + "Actions")?.Elements().ToArray();
            if (principals?.Length != 1 || actions?.Length != 1)
                return false;
            XElement principal = principals[0];
            XElement action = actions[0];
            return principal.Element(ns + "RunLevel")?.Value == "HighestAvailable" &&
                   principal.Element(ns + "LogonType")?.Value == "InteractiveToken" &&
                   IsTaskUser(principal.Element(ns + "UserId")?.Value, userSid) &&
                   action.Name == ns + "Exec" &&
                   string.IsNullOrWhiteSpace(action.Element(ns + "Arguments")?.Value) &&
                   IsStartupTargetForDirectory(action.Element(ns + "Command")?.Value, directory);
        }

        private static bool IsTaskUser(string taskUser, string userSid)
        {
            if (string.IsNullOrWhiteSpace(taskUser))
                return false;
            if (string.Equals(taskUser, userSid, StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                return ((SecurityIdentifier)new NTAccount(taskUser).Translate(typeof(SecurityIdentifier))).Value == userSid;
            }
            catch (IdentityNotMappedException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        internal static async Task<bool> TryStartHighPrivilegeDaemonAsync()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string taskName in new[] { CurrentTaskName, LegacyTaskName })
            {
                XDocument document = await Task.Run(() => ReadStartupTask(taskName));
                if (!CanRunElevatedStartupTask(document, directory, identity.User.Value))
                    continue;

                var info = new ProcessStartInfo(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                info.ArgumentList.Add("/Run");
                info.ArgumentList.Add("/TN");
                info.ArgumentList.Add(taskName);
                using Process process = Process.Start(info);
                if (process == null)
                    continue;
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                    return false;
                }
                await Task.WhenAll(output, error);
                if (process.ExitCode != 0)
                    continue;

                // Task Scheduler accepting a request does not establish the daemon's privilege.
                var wait = Stopwatch.StartNew();
                while (wait.Elapsed < TimeSpan.FromSeconds(10))
                {
                    if (DaemonStartup.IsRunning() &&
                        DaemonStartup.TryGetRunningElevation(directory, out bool elevated))
                        return elevated;
                    await Task.Delay(100);
                }
                return false;
            }
            return false;
        }

        internal static bool ShouldUseHighPrivilegeStartup(bool configured,
            bool currentTaskRegistered, bool legacyTaskRegistered)
        {
            return configured && (currentTaskRegistered || legacyTaskRegistered);
        }

        public static bool GetHighPrivilegeStartupStatus()
        {
            if (!IsRunAsAdmin)
                return false;

            bool currentTaskRegistered =
                IsStartupTaskForCurrentInstallation(CurrentTaskName);
            bool legacyTaskRegistered = !currentTaskRegistered &&
                IsStartupTaskForCurrentInstallation(LegacyTaskName);
            return ShouldUseHighPrivilegeStartup(true, currentTaskRegistered,
                legacyTaskRegistered);
        }

        public static bool GetStartupStatus()
        {
            try
            {
                string startupLnkPath = StartupLnkPath;
                if (File.Exists(startupLnkPath))
                {
                    var targetPath = ShortcutHelper.GetTargetPath(startupLnkPath);
                    var daemonPath = DaemonPath;
                    if (!string.Equals(Path.GetFullPath(daemonPath), Path.GetFullPath(targetPath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        CreateLnk(startupLnkPath, daemonPath);
                    }
                    try
                    {
                        TryDeleteOwnedLegacyStartupLink();
                    }
                    catch
                    {
                        File.Delete(startupLnkPath);
                        throw;
                    }
                    return true;
                }

                return TryMigrateLegacyStartupLink();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, LocalizationProvider.Instance.GetTextValue("Messages.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public static bool EnableNormalStartup()
        {
            try
            {
                CreateLnk(StartupLnkPath, DaemonPath);
                try
                {
                    TryDeleteOwnedLegacyStartupLink();
                }
                catch
                {
                    File.Delete(StartupLnkPath);
                    throw;
                }
                return true;
            }
            catch (Exception exception)
            {
                GestureSign.Common.Log.Logging.LogAndNotice(exception);
                return false;
            }
        }

        public static bool DisableNormalStartup()
        {
            if (File.Exists(StartupLnkPath))
            {
                try
                {
                    File.Delete(StartupLnkPath);
                }
                catch (Exception exception)
                {
                    GestureSign.Common.Log.Logging.LogAndNotice(exception);
                    return false;
                }
            }
            try
            {
                TryDeleteOwnedLegacyStartupLink();
                return true;
            }
            catch (Exception exception)
            {
                GestureSign.Common.Log.Logging.LogAndNotice(exception);
                return false;
            }
        }

        public static bool EnableHighPrivilegeStartup()
        {
            return AddStartupTask(DaemonPath);
        }

        public static void TryMigrateHighPrivilegeStartup()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    if (principal.IsInRole(WindowsBuiltInRole.Administrator) &&
                        TaskExists(LegacyTaskName))
                        AddStartupTask(DaemonPath);
                }
                catch (Exception exception)
                {
                    GestureSign.Common.Log.Logging.LogException(exception);
                }
            });
        }

        public static bool DisableHighPrivilegeStartup()
        {
            return DelStartupTask();
        }
    }
}

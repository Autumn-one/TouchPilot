using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GestureSign.Updater
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();

            try
            {
                UpdateArguments arguments = UpdateArguments.Parse(args);
                WaitForProcess(arguments.WaitProcessId, TimeSpan.FromSeconds(45));
                WaitForApplicationProcesses(TimeSpan.FromSeconds(45));

                if (arguments.Mode == UpdateMode.Installer)
                {
                    new InstallerUpdateRunner().Install(arguments.PackagePath,
                        arguments.ExpectedPackageSha256);
                }
                else
                {
                    new UpdateInstaller().Install(arguments.PackagePath, arguments.TargetDirectory,
                        arguments.ExpectedVersion, arguments.ExpectedPackageSha256);
                }

                string restartPath = GetSafeRestartPath(arguments.TargetDirectory, arguments.RestartExecutable);
                Process.Start(new ProcessStartInfo
                {
                    FileName = restartPath,
                    WorkingDirectory = arguments.TargetDirectory,
                    UseShellExecute = true
                });
                return 0;
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                return 2;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.ToString(), "TouchPilot Update Failed", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void WaitForProcess(int processId, TimeSpan timeout)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                    throw new TimeoutException("TouchPilot did not exit before the update timeout.");
            }
            catch (ArgumentException)
            {
            }
        }

        private static void WaitForApplicationProcesses(TimeSpan timeout)
        {
            string[] processNames =
            {
                "GestureSign",
                "GestureSign.ControlPanel",
                "TouchPilot",
                "TouchPilot.ControlPanel"
            };
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                Process[] processes = processNames.SelectMany(Process.GetProcessesByName)
                    .Where(process => process.Id != Environment.ProcessId)
                    .ToArray();
                if (processes.Length == 0)
                    return;

                foreach (Process process in processes)
                    process.Dispose();
                System.Threading.Thread.Sleep(200);
            }

            Process[] remainingProcesses = processNames.SelectMany(Process.GetProcessesByName)
                .Where(process => process.Id != Environment.ProcessId)
                .ToArray();
            try
            {
                if (remainingProcesses.Any())
                    throw new TimeoutException("TouchPilot processes did not exit before the update timeout.");
            }
            finally
            {
                foreach (Process process in remainingProcesses)
                    process.Dispose();
            }
        }

        private static string GetSafeRestartPath(string targetDirectory, string restartExecutable)
        {
            if (Path.IsPathRooted(restartExecutable) || restartExecutable.IndexOfAny(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
                throw new InvalidDataException("The restart executable must be a file in the installation directory.");

            string path = Path.Combine(targetDirectory, restartExecutable);
            if (!File.Exists(path))
                throw new FileNotFoundException("The updated TouchPilot executable was not found.", path);
            return path;
        }
    }
}

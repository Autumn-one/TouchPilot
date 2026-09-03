using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
                using var progressForm = new InstallationProgressForm(arguments.ExpectedVersion);
                int exitCode = 1;
                int running = 0;

                async Task RunUpdateAsync()
                {
                    if (Interlocked.Exchange(ref running, 1) != 0)
                        return;

                    progressForm.ShowPreparing();
                    try
                    {
                        await Task.Run(() => InstallAndRestart(arguments, progressForm))
                            .ConfigureAwait(true);
                        exitCode = 0;
                        progressForm.ShowCompleted();
                        await Task.Delay(300).ConfigureAwait(true);
                        progressForm.CloseForApplication();
                    }
                    catch (Exception exception)
                    {
                        exitCode = exception is Win32Exception win32Exception &&
                                   win32Exception.NativeErrorCode == 1223 ? 2 : 1;
                        progressForm.ShowRetry(exception.Message);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref running, 0);
                    }
                }

                progressForm.Shown += (sender, eventArgs) => _ = RunUpdateAsync();
                progressForm.RetryRequested += (sender, eventArgs) => _ = RunUpdateAsync();
                Application.Run(progressForm);
                return exitCode;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.ToString(), "TouchPilot Update Failed", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void InstallAndRestart(UpdateArguments arguments,
            InstallationProgressForm progressForm)
        {
            WaitForProcess(arguments.WaitProcessId, TimeSpan.FromSeconds(45));
            WaitForApplicationProcesses(arguments.TargetDirectory, TimeSpan.FromSeconds(45));
            progressForm.ShowInstalling(arguments.Mode);

            if (arguments.Mode == UpdateMode.Installer)
            {
                new InstallerUpdateRunner().Install(arguments.PackagePath,
                    arguments.ExpectedPackageSha256);
            }
            else
            {
                var progress = new InlineProgress(progressForm.ReportProgress);
                new UpdateInstaller().Install(arguments.PackagePath, arguments.TargetDirectory,
                    arguments.ExpectedVersion, arguments.ExpectedPackageSha256, progress);
            }

            string restartPath = GetSafeRestartPath(arguments.TargetDirectory,
                arguments.RestartExecutable);
            Process.Start(new ProcessStartInfo
            {
                FileName = restartPath,
                WorkingDirectory = arguments.TargetDirectory,
                UseShellExecute = true
            });
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

        private static void WaitForApplicationProcesses(string targetDirectory, TimeSpan timeout)
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
                Process[] processes = GetTargetApplicationProcesses(processNames, targetDirectory);
                if (processes.Length == 0)
                    return;

                foreach (Process process in processes)
                    process.Dispose();
                System.Threading.Thread.Sleep(200);
            }

            Process[] remainingProcesses = GetTargetApplicationProcesses(processNames, targetDirectory);
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

        private static Process[] GetTargetApplicationProcesses(IEnumerable<string> processNames,
            string targetDirectory)
        {
            var matches = new List<Process>();
            foreach (Process process in processNames.SelectMany(Process.GetProcessesByName))
            {
                if (process.Id == Environment.ProcessId)
                {
                    process.Dispose();
                    continue;
                }

                try
                {
                    if (IsExecutableInTargetDirectory(process.MainModule?.FileName, targetDirectory))
                    {
                        matches.Add(process);
                        continue;
                    }
                }
                catch (Exception exception) when (exception is Win32Exception ||
                                                  exception is InvalidOperationException ||
                                                  exception is NotSupportedException)
                {
                }

                process.Dispose();
            }

            return matches.ToArray();
        }

        internal static bool IsExecutableInTargetDirectory(string executablePath, string targetDirectory)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(targetDirectory))
                return false;

            string executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
            string expectedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory));
            return string.Equals(Path.TrimEndingDirectorySeparator(executableDirectory), expectedDirectory,
                StringComparison.OrdinalIgnoreCase);
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

        private sealed class InlineProgress : IProgress<double>
        {
            private readonly Action<double> _report;

            public InlineProgress(Action<double> report)
            {
                _report = report;
            }

            public void Report(double value)
            {
                _report(value);
            }
        }
    }
}

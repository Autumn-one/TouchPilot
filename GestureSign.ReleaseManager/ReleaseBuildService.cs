using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.ReleaseManager
{
    internal sealed class ReleaseBuildService
    {
        public async Task BuildAsync(string sourceDirectory, string configuration, string runtime,
            string version, string repository, string packagePath, IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            string publishScript = Path.Combine(sourceDirectory, "publish.ps1");
            if (!File.Exists(publishScript))
                throw new FileNotFoundException("publish.ps1 was not found in the selected source directory.",
                    publishScript);

            string outputDirectory = Path.Combine(sourceDirectory, "artifacts", "release-manager", "publish",
                runtime);
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath));

            var startInfo = new ProcessStartInfo
            {
                FileName = FindPowerShell(),
                WorkingDirectory = sourceDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(publishScript);
            startInfo.ArgumentList.Add("-Runtime");
            startInfo.ArgumentList.Add(runtime);
            startInfo.ArgumentList.Add("-Configuration");
            startInfo.ArgumentList.Add(configuration);
            startInfo.ArgumentList.Add("-OutputDirectory");
            startInfo.ArgumentList.Add(outputDirectory);
            startInfo.ArgumentList.Add("-Version");
            startInfo.ArgumentList.Add(version);
            startInfo.ArgumentList.Add("-Repository");
            startInfo.ArgumentList.Add(repository);
            startInfo.ArgumentList.Add("-PackagePath");
            startInfo.ArgumentList.Add(packagePath);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                    progress?.Report(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                    progress?.Report("ERROR: " + args.Data);
            };

            if (!process.Start())
                throw new InvalidOperationException("Unable to start the publish process.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("publish.ps1 failed with exit code " + process.ExitCode + ".");
        }

        private static string FindPowerShell()
        {
            const string installedPowerShell = @"C:\Program Files\PowerShell\7\pwsh.exe";
            return File.Exists(installedPowerShell) ? installedPowerShell : "pwsh.exe";
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace GestureSign.Updater
{
    internal sealed class InstallerUpdateRunner
    {
        internal const string SilentArguments =
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS";

        private readonly IInstallerProcessRunner _processRunner;

        public InstallerUpdateRunner()
            : this(new ShellInstallerProcessRunner())
        {
        }

        internal InstallerUpdateRunner(IInstallerProcessRunner processRunner)
        {
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        }

        public void Install(string packagePath, string expectedSha256)
        {
            if (!File.Exists(packagePath))
                throw new FileNotFoundException("The downloaded installer was not found.", packagePath);
            if (!string.Equals(Path.GetExtension(packagePath), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installer update package must be an executable.");
            if (string.IsNullOrWhiteSpace(expectedSha256) || expectedSha256.Length != 64 ||
                !string.Equals(ComputeSha256(packagePath), expectedSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installer failed SHA-256 verification.");

            int exitCode = _processRunner.Run(packagePath, SilentArguments);
            if (exitCode != 0)
                throw new InvalidOperationException("The installer exited with code " + exitCode + ".");
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }
    }

    internal interface IInstallerProcessRunner
    {
        int Run(string path, string arguments);
    }

    internal sealed class ShellInstallerProcessRunner : IInstallerProcessRunner
    {
        public int Run(string path, string arguments)
        {
            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            }) ?? throw new InvalidOperationException("The installer process could not be started.");
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}

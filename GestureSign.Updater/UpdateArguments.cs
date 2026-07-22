using System;
using System.Collections.Generic;
using System.IO;

namespace GestureSign.Updater
{
    internal sealed class UpdateArguments
    {
        public string PackagePath { get; private set; }

        public string TargetDirectory { get; private set; }

        public string RestartExecutable { get; private set; }

        public string ExpectedVersion { get; private set; }

        public string ExpectedPackageSha256 { get; private set; }

        public int WaitProcessId { get; private set; }

        public static UpdateArguments Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Updater arguments must use --name value pairs.");

                values[args[index].Substring(2)] = args[index + 1];
            }

            string packagePath = GetRequired(values, "package");
            string targetDirectory = GetRequired(values, "target");
            string restartExecutable = GetRequired(values, "restart");
            string expectedVersion = GetRequired(values, "version");
            string expectedPackageSha256 = GetRequired(values, "sha256");
            if (expectedPackageSha256.Length != 64 || !IsHexString(expectedPackageSha256))
                throw new ArgumentException("--sha256 must be a SHA-256 hash.");
            if (!int.TryParse(GetRequired(values, "wait-pid"), out int waitProcessId) || waitProcessId <= 0)
                throw new ArgumentException("--wait-pid must be a positive process ID.");

            return new UpdateArguments
            {
                PackagePath = Path.GetFullPath(packagePath),
                TargetDirectory = Path.GetFullPath(targetDirectory),
                RestartExecutable = restartExecutable,
                ExpectedVersion = expectedVersion,
                ExpectedPackageSha256 = expectedPackageSha256.ToLowerInvariant(),
                WaitProcessId = waitProcessId
            };
        }

        private static bool IsHexString(string value)
        {
            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character))
                    return false;
            }

            return true;
        }

        private static string GetRequired(IReadOnlyDictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out string value) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Missing required updater argument --" + key + ".");
            return value.Trim();
        }
    }
}

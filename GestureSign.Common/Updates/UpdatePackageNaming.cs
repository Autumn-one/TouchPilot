using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace GestureSign.Common.Updates
{
    public static class UpdatePackageNaming
    {
        public const string MetadataAssetName = "TouchPilot-update.json";
        public const string WindowsX64Runtime = "win-x64";
        public const string InstallerDistribution = "installer";
        public const string PortableDistribution = "portable";

        public static string GetCurrentRuntimeIdentifier()
        {
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X86:
                    return "win-x86";
                case Architecture.Arm64:
                    return "win-arm64";
                default:
                    return "win-x64";
            }
        }

        public static string GetAssetName(string version, string runtimeIdentifier)
        {
            return $"GestureSign-{version}-{runtimeIdentifier}.zip";
        }

        public static string GetChecksumAssetName(string version, string runtimeIdentifier)
        {
            return GetAssetName(version, runtimeIdentifier) + ".sha256";
        }

        public static string GetInstallerAssetName(string version)
        {
            return $"TouchPilot-{version}-{WindowsX64Runtime}-setup.exe";
        }

        public static string GetPortableAssetName(string version)
        {
            return $"TouchPilot-{version}-{WindowsX64Runtime}-portable.zip";
        }

        public static bool IsDistributionSupported(string distribution)
        {
            return string.Equals(distribution, InstallerDistribution, StringComparison.Ordinal) ||
                   string.Equals(distribution, PortableDistribution, StringComparison.Ordinal);
        }

        public static GitHubReleaseAsset FindAsset(GitHubReleaseInfo release, string assetName)
        {
            if (release == null)
                throw new ArgumentNullException(nameof(release));

            return release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase));
        }
    }
}

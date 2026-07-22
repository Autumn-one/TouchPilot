using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace GestureSign.Common.Updates
{
    public static class UpdatePackageNaming
    {
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

        public static GitHubReleaseAsset FindAsset(GitHubReleaseInfo release, string assetName)
        {
            if (release == null)
                throw new ArgumentNullException(nameof(release));

            return release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase));
        }
    }
}

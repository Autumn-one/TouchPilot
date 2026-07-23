using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NuGet.Versioning;

namespace GestureSign.Common.Updates
{
    public static class UpdateInstallation
    {
        public static bool IsSelfUpdateSupported
        {
            get
            {
#if DEBUG || ConvertedDesktopApp
                return false;
#else
                return true;
#endif
            }
        }

        public static ReleaseManifest GetCurrentManifest()
        {
            return ReleaseManifest.TryLoadFromDirectory(AppContext.BaseDirectory);
        }

        public static NuGetVersion GetCurrentVersion(Assembly entryAssembly)
        {
            ReleaseManifest manifest = GetCurrentManifest();
            if (manifest != null && ReleaseVersion.TryParse(manifest.Version, out NuGetVersion manifestVersion))
                return manifestVersion;

            Version assemblyVersion = entryAssembly?.GetName().Version ?? new Version(0, 0);
            return new NuGetVersion(Math.Max(0, assemblyVersion.Major),
                Math.Max(0, assemblyVersion.Minor), 0);
        }

        public static string GetCurrentVersionText(Assembly entryAssembly)
        {
            return ReleaseVersion.ToReleaseString(GetCurrentVersion(entryAssembly));
        }

        public static GitHubRepository GetRepository()
        {
            ReleaseManifest manifest = GetCurrentManifest();
            string value = manifest?.Repository;
            return GitHubRepository.TryParse(value, out GitHubRepository repository)
                ? repository
                : GitHubRepository.Parse(Constants.DefaultGitHubRepository);
        }

        public static string GetCurrentDistribution()
        {
            string distribution = GetCurrentManifest()?.Distribution;
            if (UpdatePackageNaming.IsDistributionSupported(distribution))
                return distribution;

#if Portable
            return UpdatePackageNaming.PortableDistribution;
#else
            return UpdatePackageNaming.InstallerDistribution;
#endif
        }

        public static UpdateAssetMetadata FindAsset(UpdateMetadata metadata, string distribution,
            string runtime)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));
            if (!UpdatePackageNaming.IsDistributionSupported(distribution))
                throw new ArgumentException("The update distribution is not supported.", nameof(distribution));
            if (string.IsNullOrWhiteSpace(runtime))
                throw new ArgumentException("An update runtime is required.", nameof(runtime));

            UpdateAssetMetadata[] matches = metadata.Assets?.Where(asset => asset != null &&
                    string.Equals(asset.Distribution, distribution, StringComparison.Ordinal) &&
                    string.Equals(asset.Runtime, runtime, StringComparison.Ordinal))
                .ToArray() ?? Array.Empty<UpdateAssetMetadata>();
            if (matches.Length != 1)
                throw new InvalidDataException(
                    $"The update metadata does not contain one {distribution}/{runtime} asset.");
            return matches[0];
        }
    }
}

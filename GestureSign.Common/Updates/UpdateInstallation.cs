using System;
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
    }
}

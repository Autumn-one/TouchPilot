using System;
using NuGet.Versioning;

namespace GestureSign.Common.Updates
{
    public static class ReleaseVersion
    {
        public static NuGetVersion Parse(string value)
        {
            if (!TryParse(value, out NuGetVersion version))
                throw new FormatException(
                    "Release version must use SemVer such as 0.0.1 or v0.0.2-beta.1.");

            return version;
        }

        public static bool TryParse(string value, out NuGetVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(1);

            return NuGetVersion.TryParseStrict(normalized, out version);
        }

        public static string ToReleaseString(NuGetVersion version)
        {
            if (version == null)
                throw new ArgumentNullException(nameof(version));

            return version.ToFullString();
        }
    }
}

using System;

namespace GestureSign.Common.Updates
{
    public static class ReleaseVersion
    {
        public static Version Parse(string value)
        {
            if (!TryParse(value, out Version version))
                throw new FormatException("Release version must use a numeric format such as 8.2.0 or v8.2.0.");

            return version;
        }

        public static bool TryParse(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(1);

            int metadataIndex = normalized.IndexOfAny(new[] { '-', '+' });
            if (metadataIndex >= 0)
                normalized = normalized.Substring(0, metadataIndex);

            if (!Version.TryParse(normalized, out Version parsed) || parsed.Major < 0 || parsed.Minor < 0)
                return false;

            int build = parsed.Build < 0 ? 0 : parsed.Build;
            int revision = parsed.Revision < 0 ? 0 : parsed.Revision;
            version = new Version(parsed.Major, parsed.Minor, build, revision);
            return true;
        }

        public static string ToReleaseString(Version version)
        {
            if (version == null)
                throw new ArgumentNullException(nameof(version));

            return version.Revision == 0
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : version.ToString(4);
        }
    }
}

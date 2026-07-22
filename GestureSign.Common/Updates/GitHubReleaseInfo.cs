using System;
using System.Collections.Generic;

namespace GestureSign.Common.Updates
{
    public sealed class GitHubReleaseInfo
    {
        public long Id { get; set; }

        public string TagName { get; set; }

        public string Name { get; set; }

        public string Body { get; set; }

        public string HtmlUrl { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }

        public IReadOnlyList<GitHubReleaseAsset> Assets { get; set; } = Array.Empty<GitHubReleaseAsset>();

        public Version Version => ReleaseVersion.Parse(TagName);
    }

    public sealed class GitHubReleaseAsset
    {
        public long Id { get; set; }

        public string Name { get; set; }

        public string DownloadUrl { get; set; }

        public long Size { get; set; }
    }
}

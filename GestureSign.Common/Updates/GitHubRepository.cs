using System;
using System.Linq;

namespace GestureSign.Common.Updates
{
    public sealed class GitHubRepository
    {
        private GitHubRepository(string owner, string name)
        {
            Owner = owner;
            Name = name;
        }

        public string Owner { get; }

        public string Name { get; }

        public string Slug => Owner + "/" + Name;

        public static GitHubRepository Parse(string value)
        {
            if (!TryParse(value, out GitHubRepository repository))
                throw new FormatException("GitHub repository must be formatted as owner/repository or a github.com URL.");

            return repository;
        }

        public static bool TryParse(string value, out GitHubRepository repository)
        {
            repository = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string candidate = value.Trim();
            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri))
            {
                if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase))
                    return false;

                candidate = uri.AbsolutePath.Trim('/');
            }

            string[] parts = candidate.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return false;

            string owner = parts[0].Trim();
            string name = parts[1].Trim();
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            if (!IsValidSegment(owner) || !IsValidSegment(name))
                return false;

            repository = new GitHubRepository(owner, name);
            return true;
        }

        private static bool IsValidSegment(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.All(character => char.IsLetterOrDigit(character) || character == '-' ||
                                          character == '_' || character == '.');
        }
    }
}

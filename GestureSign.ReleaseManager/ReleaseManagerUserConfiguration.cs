using GestureSign.Common.Updates;
using System;
using System.IO;
using System.Text.Json;

namespace GestureSign.ReleaseManager
{
    internal sealed class ReleaseManagerUserConfiguration
    {
        internal const string FileName = "TouchPilot.ReleaseManager.config.user";
        private const int MaximumConfigurationBytes = 64 * 1024;

        private ReleaseManagerUserConfiguration(string repository, string token)
        {
            Repository = repository;
            Token = token;
        }

        public string Repository { get; }
        public string Token { get; }

        public static ReleaseManagerUserConfiguration Load(string sourceDirectory)
        {
            ReleaseManagerUserConfiguration configuration = TryLoad(sourceDirectory);
            return configuration ?? throw new FileNotFoundException(
                "The Release Manager user configuration was not found.",
                GetPath(sourceDirectory));
        }

        public static ReleaseManagerUserConfiguration TryLoad(string sourceDirectory)
        {
            string path = GetPath(sourceDirectory);
            if (!File.Exists(path))
                return null;
            var file = new FileInfo(path);
            if (file.Length <= 0 || file.Length > MaximumConfigurationBytes)
                throw new InvalidDataException("The Release Manager user configuration has an invalid size.");

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                string token = FindString(document.RootElement, IsExactTokenName) ??
                               FindString(document.RootElement, IsTokenLikeName);
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidDataException(
                        "The Release Manager user configuration does not contain a token.");

                string repository = FindString(document.RootElement, IsRepositoryName) ??
                                    "Autumn-one/TouchPilot";
                return new ReleaseManagerUserConfiguration(
                    GitHubRepository.Parse(repository).Slug, token.Trim());
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The Release Manager user configuration is not valid JSON.", exception);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    "The Release Manager user configuration contains an invalid repository.", exception);
            }
        }

        private static string FindString(JsonElement element, Func<string, bool> propertyMatcher)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String &&
                        propertyMatcher(property.Name))
                        return property.Value.GetString();
                }
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string nested = FindString(property.Value, propertyMatcher);
                    if (nested != null)
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string nested = FindString(item, propertyMatcher);
                    if (nested != null)
                        return nested;
                }
            }
            return null;
        }

        private static bool IsExactTokenName(string name)
        {
            string normalized = Normalize(name);
            return normalized == "token" || normalized == "githubtoken" ||
                   normalized == "accesstoken" || normalized == "personalaccesstoken" ||
                   normalized == "githubpat" || normalized == "pat";
        }

        private static bool IsTokenLikeName(string name)
        {
            return Normalize(name).Contains("token", StringComparison.Ordinal);
        }

        private static bool IsRepositoryName(string name)
        {
            string normalized = Normalize(name);
            return normalized == "repository" || normalized == "githubrepository" ||
                   normalized == "repositoryslug" || normalized == "repo";
        }

        private static string Normalize(string name)
        {
            return (name ?? string.Empty).Replace("-", string.Empty)
                .Replace("_", string.Empty).Replace(".", string.Empty)
                .ToLowerInvariant();
        }

        private static string GetPath(string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException("A source directory is required.", nameof(sourceDirectory));
            return Path.Combine(Path.GetFullPath(sourceDirectory), FileName);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GestureSign.Common.Updates
{
    public sealed class ReleaseManifest
    {
        public const string FileName = "release-manifest.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public string Version { get; set; }

        public string Repository { get; set; }

        public string Runtime { get; set; }

        public List<ReleaseFileEntry> Files { get; set; } = new List<ReleaseFileEntry>();

        public static ReleaseManifest TryLoadFromDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return null;

            string path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(path), JsonOptions);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is JsonException)
            {
                return null;
            }
        }

        public static ReleaseManifest Load(string path)
        {
            return JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(path), JsonOptions);
        }

        public void Save(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
    }

    public sealed class ReleaseFileEntry
    {
        public string Path { get; set; }

        public string Sha256 { get; set; }

        public long Size { get; set; }
    }
}

using System;
using System.Collections.Generic;

namespace GestureSign.Common.Updates
{
    public sealed class UpdateMetadata
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string Product { get; set; } = "TouchPilot";

        public string Repository { get; set; }

        public string Version { get; set; }

        public string Tag { get; set; }

        public DateTimeOffset BuiltAtUtc { get; set; }

        public DateTimeOffset ExpiresAtUtc { get; set; }

        public string ReleaseNotes { get; set; } = string.Empty;

        public List<UpdateAssetMetadata> Assets { get; set; } = new List<UpdateAssetMetadata>();
    }

    public sealed class UpdateAssetMetadata
    {
        public string Distribution { get; set; }

        public string Runtime { get; set; }

        public string Name { get; set; }

        public long Size { get; set; }

        public string Sha256 { get; set; }
    }

    public sealed class SignedUpdateMetadata
    {
        public UpdateMetadata Payload { get; set; }

        public string KeyId { get; set; }

        public string Signature { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace GestureSign.Common.Telemetry
{
    public sealed class TelemetryConfiguration
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string Repository { get; set; }

        public long Revision { get; set; }

        public DateTimeOffset PublishedAtUtc { get; set; }

        public List<TelemetryEndpoint> Endpoints { get; set; } = new List<TelemetryEndpoint>();
    }

    public sealed class TelemetryEndpoint
    {
        public string BaseAddress { get; set; }

        public int Port { get; set; }

        public string EventPath { get; set; } = "/v1/events";

        public Uri BuildEventUri()
        {
            if (!Uri.TryCreate(BaseAddress, UriKind.Absolute, out Uri baseUri))
                throw new InvalidDataException("The telemetry base address is invalid.");

            return new UriBuilder(baseUri)
            {
                Port = Port,
                Path = EventPath
            }.Uri;
        }
    }

    public sealed class SignedTelemetryConfiguration
    {
        public TelemetryConfiguration Payload { get; set; }

        public string KeyId { get; set; }

        public string Signature { get; set; }
    }

    public static class TelemetryConfigurationValidator
    {
        public static void Validate(TelemetryConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (configuration.SchemaVersion != TelemetryConfiguration.CurrentSchemaVersion)
                throw new InvalidDataException("The telemetry configuration schema is not supported.");
            if (!Updates.GitHubRepository.TryParse(configuration.Repository, out _))
                throw new InvalidDataException("The telemetry configuration repository is invalid.");
            if (configuration.Revision <= 0)
                throw new InvalidDataException("The telemetry configuration revision must be positive.");
            if (configuration.PublishedAtUtc.Offset != TimeSpan.Zero)
                throw new InvalidDataException("The telemetry configuration timestamp must use UTC.");
            if (configuration.Endpoints == null || configuration.Endpoints.Count == 0 ||
                configuration.Endpoints.Count > 4)
                throw new InvalidDataException("The telemetry configuration must contain one to four endpoints.");

            var uniqueEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TelemetryEndpoint endpoint in configuration.Endpoints)
            {
                if (endpoint == null || endpoint.Port <= 0 || endpoint.Port > 65535 ||
                    string.IsNullOrWhiteSpace(endpoint.EventPath) ||
                    !endpoint.EventPath.StartsWith("/", StringComparison.Ordinal) ||
                    endpoint.EventPath.StartsWith("//", StringComparison.Ordinal))
                    throw new InvalidDataException("A telemetry endpoint is invalid.");
                if (!Uri.TryCreate(endpoint.BaseAddress, UriKind.Absolute, out Uri baseUri) ||
                    !(baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps) ||
                    !baseUri.IsDefaultPort || !string.IsNullOrEmpty(baseUri.UserInfo) ||
                    !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment) ||
                    !string.Equals(baseUri.AbsolutePath, "/", StringComparison.Ordinal))
                    throw new InvalidDataException("A telemetry base address must be an HTTP origin without a port.");

                Uri eventUri = endpoint.BuildEventUri();
                if (!uniqueEndpoints.Add(eventUri.AbsoluteUri))
                    throw new InvalidDataException("The telemetry configuration contains duplicate endpoints.");
            }
        }
    }
}

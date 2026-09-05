using GestureSign.Common.Telemetry;
using GestureSign.Common.Updates;
using GestureSign.ReleaseManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GestureSign.Tests
{
    public class TelemetryTests
    {
        [Fact]
        public void SignedConfigurationRejectsTampering()
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string signed = TelemetryConfigurationSignature.Sign(CreateConfiguration(1), key);

            Assert.Throws<CryptographicException>(() =>
                TelemetryConfigurationSignature.Verify(
                    signed.Replace("43.159.148.243", "127.0.0.1"), key));
        }

        [Fact]
        public async Task ConfigurationClientFallsThroughAndCachesFirstTrustedSource()
        {
            using var directory = new TemporaryDirectory();
            using ECDsa trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using ECDsa untrustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string trusted = TelemetryConfigurationSignature.Sign(CreateConfiguration(2), trustedKey);
            string untrusted = TelemetryConfigurationSignature.Sign(CreateConfiguration(99), untrustedKey);
            var handler = new ConfigurationHandler(new Dictionary<string, string>
            {
                ["untrusted.invalid"] = untrusted,
                ["healthy.invalid"] = trusted,
                ["unused.invalid"] = trusted
            });
            using var httpClient = new HttpClient(handler);
            using var client = new TelemetryConfigurationClient(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), trustedKey, httpClient,
                new[]
                {
                    new UpdateSource("untrusted", "https://untrusted.invalid/"),
                    new UpdateSource("healthy", "https://healthy.invalid/"),
                    new UpdateSource("unused", "https://unused.invalid/")
                }, TimeSpan.FromSeconds(1), () => DateTimeOffset.UnixEpoch);
            string cachePath = Path.Combine(directory.Path, "cache", "endpoint.json");

            TelemetryConfigurationResult result = await client.GetAsync(cachePath,
                CancellationToken.None);

            Assert.Equal(2, result.Configuration.Revision);
            Assert.Equal("healthy", result.SourceName);
            Assert.False(result.FromCache);
            Assert.True(File.Exists(cachePath));
            Assert.Collection(handler.Requests,
                request => Assert.Equal("untrusted.invalid", request.Host),
                request => Assert.Equal("healthy.invalid", request.Host));
            Assert.All(handler.Requests, request =>
            {
                Assert.True(request.NoCache);
                Assert.Null(request.Authorization);
                Assert.Contains("/main/distribution/telemetry-endpoint.json", request.Url);
            });
        }

        [Fact]
        public async Task ConfigurationClientUsesVerifiedCacheWhenSourcesAreUnavailable()
        {
            using var directory = new TemporaryDirectory();
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string cachePath = Path.Combine(directory.Path, "endpoint.json");
            File.WriteAllText(cachePath,
                TelemetryConfigurationSignature.Sign(CreateConfiguration(7), key));
            var handler = new ConfigurationHandler(new Dictionary<string, string>());
            using var httpClient = new HttpClient(handler);
            using var client = new TelemetryConfigurationClient(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), key, httpClient,
                new[] { new UpdateSource("offline", "https://offline.invalid/") },
                TimeSpan.FromSeconds(1), () => DateTimeOffset.UnixEpoch);

            TelemetryConfigurationResult result = await client.GetAsync(cachePath,
                CancellationToken.None);

            Assert.True(result.FromCache);
            Assert.Equal("cache", result.SourceName);
            Assert.Equal(7, result.Configuration.Revision);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task ConfigurationClientNeverDowngradesVerifiedCache()
        {
            using var directory = new TemporaryDirectory();
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string cachePath = Path.Combine(directory.Path, "endpoint.json");
            File.WriteAllText(cachePath,
                TelemetryConfigurationSignature.Sign(CreateConfiguration(7), key));
            var handler = new ConfigurationHandler(new Dictionary<string, string>
            {
                ["stale.invalid"] = TelemetryConfigurationSignature.Sign(
                    CreateConfiguration(6), key),
                ["current.invalid"] = TelemetryConfigurationSignature.Sign(
                    CreateConfiguration(8, "https://new-server.example"), key)
            });
            using var httpClient = new HttpClient(handler);
            using var client = new TelemetryConfigurationClient(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), key, httpClient,
                new[]
                {
                    new UpdateSource("stale", "https://stale.invalid/"),
                    new UpdateSource("current", "https://current.invalid/")
                }, TimeSpan.FromSeconds(1), () => DateTimeOffset.UnixEpoch);

            TelemetryConfigurationResult result = await client.GetAsync(cachePath,
                CancellationToken.None);

            Assert.False(result.FromCache);
            Assert.Equal(8, result.Configuration.Revision);
            Assert.Equal("https://new-server.example:4318/v1/events",
                Assert.Single(result.Configuration.Endpoints).BuildEventUri().AbsoluteUri);
            Assert.Collection(handler.Requests,
                request => Assert.Equal("stale.invalid", request.Host),
                request => Assert.Equal("current.invalid", request.Host));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FailedDeliverySkipsSameRevisionMirrorButRetainsOfflineFallback(bool hasReplacement)
        {
            using var directory = new TemporaryDirectory();
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string cachePath = Path.Combine(directory.Path, "endpoint.json");
            string oldJson = TelemetryConfigurationSignature.Sign(CreateConfiguration(7), key);
            File.WriteAllText(cachePath, oldJson);
            var handler = new ConfigurationHandler(new Dictionary<string, string>
            {
                ["old.invalid"] = oldJson,
                ["new.invalid"] = hasReplacement
                    ? TelemetryConfigurationSignature.Sign(CreateConfiguration(8, "https://new.example"), key)
                    : oldJson
            });
            using var http = new HttpClient(handler);
            using var client = new TelemetryConfigurationClient(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), key, http,
                new[]
                {
                    new UpdateSource("old", "https://old.invalid/"),
                    new UpdateSource("new", "https://new.invalid/")
                }, TimeSpan.FromSeconds(1), () => DateTimeOffset.UtcNow);

            TelemetryConfigurationResult result = await client.GetAsync(cachePath, CancellationToken.None, 7);

            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal(hasReplacement ? 8 : 7, result.Configuration.Revision);
            Assert.Equal(!hasReplacement, result.FromCache);
        }

        [Fact]
        public void EndpointCombinesBaseAddressPortAndEventPath()
        {
            TelemetryEndpoint endpoint = CreateConfiguration(1).Endpoints[0];

            Assert.Equal("http://43.159.148.243:4318/v1/events", endpoint.BuildEventUri().AbsoluteUri);
        }

        [Fact]
        public void CommittedConfigurationMatchesClientTrustAndOfficialRepository()
        {
            string root = FindRepositoryRoot();
            string path = Path.Combine(root,
                TelemetryConfigurationClient.RepositoryPath.Replace('/', Path.DirectorySeparatorChar));
            using ECDsa trustedKey = TrustedUpdateSigningKey.Load();

            TelemetryConfiguration configuration = TelemetryConfigurationSignature.Verify(
                File.ReadAllText(path), trustedKey);

            Assert.Equal("Autumn-one/TouchPilot", configuration.Repository);
            Assert.True(configuration.Revision > 0);
            Assert.NotEmpty(configuration.Endpoints);
            Assert.All(configuration.Endpoints, endpoint => Assert.True(endpoint.BuildEventUri().IsAbsoluteUri));
        }

        [Fact]
        public void ConfigurationBuilderIncrementsExistingRevision()
        {
            using var directory = new TemporaryDirectory();
            File.WriteAllText(Path.Combine(directory.Path, "GestureSign.sln"), string.Empty);
            var builder = new TelemetryConfigurationBuilder();

            string path = builder.Build(directory.Path, "Autumn-one/TouchPilot",
                "http://43.159.148.243", 4318);
            builder.Build(directory.Path, "Autumn-one/TouchPilot",
                "http://43.159.148.243", 4318);

            using ECDsa key = new ReleaseSigningKeyStore(directory.Path).LoadOrCreate();
            TelemetryConfiguration configuration = TelemetryConfigurationSignature.Verify(
                File.ReadAllText(path), key);
            Assert.Equal(2, configuration.Revision);
        }

        [Fact]
        public void ReporterRejectsEventNamesOutsideStableSchema()
        {
            using var reporter = new TelemetryReporter(
                GitHubRepository.Parse("Autumn-one/TouchPilot"), Path.GetTempPath(),
                "8.2.0", "installer", "win-x64", _ => { }, new HttpClient());

            Assert.Throws<ArgumentException>(() => reporter.Track("Window Title"));
        }

        private static TelemetryConfiguration CreateConfiguration(long revision,
            string baseAddress = "http://43.159.148.243")
        {
            return new TelemetryConfiguration
            {
                Repository = "Autumn-one/TouchPilot",
                Revision = revision,
                PublishedAtUtc = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero),
                Endpoints = new List<TelemetryEndpoint>
                {
                    new TelemetryEndpoint
                    {
                        BaseAddress = baseAddress,
                        Port = 4318,
                        EventPath = "/v1/events"
                    }
                }
            };
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GestureSign.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("The TouchPilot repository root was not found.");
        }

        private sealed class ConfigurationHandler : HttpMessageHandler
        {
            private readonly IReadOnlyDictionary<string, string> _responses;

            public ConfigurationHandler(IReadOnlyDictionary<string, string> responses)
            {
                _responses = responses;
            }

            public List<(string Host, string Url, bool NoCache, string Authorization)> Requests { get; } =
                new List<(string, string, bool, string)>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests.Add((request.RequestUri.Host, request.RequestUri.ToString(),
                    request.Headers.CacheControl?.NoCache == true,
                    request.Headers.Authorization?.ToString()));
                if (_responses.TryGetValue(request.RequestUri.Host, out string json))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "TouchPilot.TelemetryTests." + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, true);
                }
                catch
                {
                }
            }
        }
    }
}

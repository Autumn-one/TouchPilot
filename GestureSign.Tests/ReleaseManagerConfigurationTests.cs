using GestureSign.ReleaseManager;
using System;
using System.IO;
using Xunit;

namespace GestureSign.Tests
{
    public class ReleaseManagerConfigurationTests
    {
        [Fact]
        public void UserConfigurationLoadsNestedRepositoryAndToken()
        {
            using var directory = new ConfigurationTemporaryDirectory();
            File.WriteAllText(Path.Combine(directory.Path,
                ReleaseManagerUserConfiguration.FileName),
                "{\"github\":{\"repository\":\"Autumn-one/TouchPilot\"," +
                "\"githubToken\":\"test-token-value\"}}");

            ReleaseManagerUserConfiguration configuration =
                ReleaseManagerUserConfiguration.Load(directory.Path);

            Assert.Equal("Autumn-one/TouchPilot", configuration.Repository);
            Assert.Equal("test-token-value", configuration.Token);
        }

        [Fact]
        public void UserConfigurationUsesTouchPilotRepositoryWhenOnlyTokenIsPresent()
        {
            using var directory = new ConfigurationTemporaryDirectory();
            File.WriteAllText(Path.Combine(directory.Path,
                ReleaseManagerUserConfiguration.FileName), "{\"token\":\"test-token-value\"}");

            ReleaseManagerUserConfiguration configuration =
                ReleaseManagerUserConfiguration.Load(directory.Path);

            Assert.Equal("Autumn-one/TouchPilot", configuration.Repository);
        }

        [Fact]
        public void UserConfigurationRejectsMissingTokenWithoutEchoingValues()
        {
            using var directory = new ConfigurationTemporaryDirectory();
            const string privateValue = "must-not-appear-in-errors";
            File.WriteAllText(Path.Combine(directory.Path,
                ReleaseManagerUserConfiguration.FileName),
                "{\"repository\":\"Autumn-one/TouchPilot\",\"unrelated\":\"" +
                privateValue + "\"}");

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                ReleaseManagerUserConfiguration.Load(directory.Path));

            Assert.DoesNotContain(privateValue, exception.ToString());
        }

        private sealed class ConfigurationTemporaryDirectory : IDisposable
        {
            public ConfigurationTemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "TouchPilot.ConfigurationTests." + Guid.NewGuid().ToString("N"));
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

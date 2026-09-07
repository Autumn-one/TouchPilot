using GestureSign.Common.Configuration;
using System.Configuration;
using Xunit;

namespace GestureSign.Tests
{
    public class TouchpadSettingsMigrationTests
    {
        [Theory]
        [InlineData("True", "0", "0")]
        [InlineData("True", "1", "1")]
        [InlineData("True", "2", "2")]
        [InlineData("True", "3", "3")]
        [InlineData("False", "0", "0")]
        [InlineData("False", "1", "0")]
        [InlineData("False", "2", "0")]
        [InlineData("False", "3", "0")]
        [InlineData(null, "1", "0")]
        [InlineData("invalid", "3", "0")]
        public void LegacySettingsPreserveEffectiveDragState(string edges, string mode, string expected)
        {
            var settings = new KeyValueConfigurationCollection();
            if (edges != null)
                settings.Add(nameof(AppConfig.TouchpadEdgeGesturesEnabled), edges);
            settings.Add(nameof(AppConfig.TouchpadWindowDragMode), mode);
            settings.Add(nameof(AppConfig.TouchpadWindowDragImplementation), "3");
            settings.Add(nameof(AppConfig.TouchpadWindowDragSensitivityPercent), "140");

            TouchpadSettingsMigration.Migrate(settings);

            Assert.Equal(expected, settings[nameof(AppConfig.TouchpadWindowDragMode)].Value);
            Assert.Equal(edges, settings[nameof(AppConfig.TouchpadEdgeGesturesEnabled)]?.Value);
            Assert.Equal("3", settings[nameof(AppConfig.TouchpadWindowDragImplementation)].Value);
            Assert.Equal("140", settings[nameof(AppConfig.TouchpadWindowDragSensitivityPercent)].Value);
            Assert.Equal("True", settings[TouchpadSettingsMigration.IndependentWindowDragKey].Value);
        }

        [Theory]
        [InlineData("True", "False", "1")]
        [InlineData("True", "False", "3")]
        [InlineData("False", "True", "0")]
        public void ChangingEdgeSwitchAfterMigrationDoesNotChangeDragMode(string oldEdges, string newEdges, string mode)
        {
            var settings = new KeyValueConfigurationCollection();
            settings.Add(nameof(AppConfig.TouchpadEdgeGesturesEnabled), oldEdges);
            settings.Add(nameof(AppConfig.TouchpadWindowDragMode), mode);
            TouchpadSettingsMigration.Migrate(settings);

            settings[nameof(AppConfig.TouchpadEdgeGesturesEnabled)].Value = newEdges;
            TouchpadSettingsMigration.Migrate(settings);

            Assert.Equal(mode, settings[nameof(AppConfig.TouchpadWindowDragMode)].Value);
        }

        [Fact]
        public void DragCanBeEnabledWithEdgesOffAfterMigration()
        {
            var settings = new KeyValueConfigurationCollection();
            settings.Add(nameof(AppConfig.TouchpadEdgeGesturesEnabled), "False");
            TouchpadSettingsMigration.Migrate(settings);

            settings.Add(nameof(AppConfig.TouchpadWindowDragMode), "3");
            TouchpadSettingsMigration.Migrate(settings);

            Assert.Equal("3", settings[nameof(AppConfig.TouchpadWindowDragMode)].Value);
            Assert.Equal("False", settings[nameof(AppConfig.TouchpadEdgeGesturesEnabled)].Value);
        }

        [Fact]
        public void NewSettingsDoNotEnableEitherFeature()
        {
            var settings = new KeyValueConfigurationCollection();

            TouchpadSettingsMigration.Migrate(settings);

            Assert.Null(settings[nameof(AppConfig.TouchpadWindowDragMode)]);
            Assert.Null(settings[nameof(AppConfig.TouchpadEdgeGesturesEnabled)]);
        }
    }
}

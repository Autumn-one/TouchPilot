using System.Configuration;

namespace GestureSign.Common.Configuration
{
    internal static class TouchpadSettingsMigration
    {
        internal const string IndependentWindowDragKey = "TouchpadWindowDragIndependent";

        internal static void Migrate(KeyValueConfigurationCollection settings)
        {
            if (bool.TryParse(settings[IndependentWindowDragKey]?.Value, out bool independent) && independent)
                return;

            bool edgesEnabled = bool.TryParse(settings[nameof(AppConfig.TouchpadEdgeGesturesEnabled)]?.Value,
                out bool enabled) && enabled;
            var dragMode = settings[nameof(AppConfig.TouchpadWindowDragMode)];
            if (!edgesEnabled && dragMode != null)
                dragMode.Value = "0";

            // Normalize in memory on load; the next settings save persists both values together.
            if (settings[IndependentWindowDragKey] == null)
                settings.Add(IndependentWindowDragKey, "True");
            else
                settings[IndependentWindowDragKey].Value = "True";
        }
    }
}

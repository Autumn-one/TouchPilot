using GestureSign.Common.Input;
using GestureSign.Common.Log;
using ManagedWinapi.Hooks;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Threading;

namespace GestureSign.Common.Configuration
{
    public class AppConfig
    {
        private static bool _loadFlag = true;
        private static Dictionary<string, object> _settingCache = new Dictionary<string, object>(16);
        static System.Configuration.Configuration _config;
        static Timer Timer;
        public static event EventHandler ConfigChanged;

        private static ExeConfigurationFileMap ExeMap;
        private static string _applicationDataPath;
        private static string _localApplicationDataPath;

        private static System.Configuration.Configuration Config
        {
            get
            {
                if (_config == null || _loadFlag)
                {
                    try
                    {
                        FileManager.WaitFile(ConfigPath);
                        _config = ConfigurationManager.OpenMappedExeConfiguration(ExeMap, ConfigurationUserLevel.None);
                        _settingCache.Clear();
                        _loadFlag = false;
                    }
                    catch (Exception e)
                    {
                        Logging.LogAndNotice(new Exceptions.FileWriteException(e));
                    }
                }
                return _config;
            }
        }

        public static string ApplicationDataPath
        {
            get
            {
                if (!Directory.Exists(_applicationDataPath))
                {
                    try
                    {
                        Directory.CreateDirectory(_applicationDataPath);
                    }
                    catch (Exception e)
                    {
                        Logging.LogAndNotice(new Exceptions.FileWriteException(e));
                    }
                }

                return _applicationDataPath;
            }
            private set => _applicationDataPath = value;
        }

        public static string LocalApplicationDataPath
        {
            get
            {
                if (!Directory.Exists(_localApplicationDataPath))
                {
                    try
                    {
                        Directory.CreateDirectory(_localApplicationDataPath);
                    }
                    catch (Exception e)
                    {
                        Logging.LogAndNotice(new Exceptions.FileWriteException(e));
                    }
                }

                return _localApplicationDataPath;
            }
            private set => _localApplicationDataPath = value;
        }

        public static string BackupPath { private set; get; }

        public static string ConfigPath { private set; get; }

        public static string CurrentFolderPath { private set; get; }

        #region Setting Parameters

        public static System.Drawing.Color VisualFeedbackColor
        {
            get
            {
                return (System.Drawing.Color)GetValue("VisualFeedbackColor", System.Drawing.Color.DeepSkyBlue);
            }
            set
            {
                SetValue("VisualFeedbackColor", value);
            }
        }

        public static int VisualFeedbackWidth
        {
            get
            {
                return (int)GetValue("VisualFeedbackWidth", 9);
            }
            set
            {
                SetValue("VisualFeedbackWidth", value);
            }
        }
        public static int MinimumPointDistance
        {
            get
            {
                return (int)GetValue("MinimumPointDistance", 20);
            }
            set
            {
                SetValue("MinimumPointDistance", value);
            }
        }

        public static double Opacity
        {
            get
            {
                return (double)GetValue("Opacity", 0.35);
            }
            set
            {
                SetValue("Opacity", value);
            }
        }

        public static bool IsOrderByLocation
        {
            get
            {
                return (bool)GetValue("IsOrderByLocation", true);
            }
            set
            {
                SetValue("IsOrderByLocation", value);
            }
        }

        public static bool UiAccess { get; set; }
        public static bool ShowTrayIcon
        {
            get
            {
                return (bool)GetValue("ShowTrayIcon", true);
            }
            set
            {
                SetValue("ShowTrayIcon", value);
            }
        }

        public static string CultureName
        {
            get
            {
                return (string)GetValue("CultureName", "");
            }
            set
            {
                SetValue("CultureName", value);
            }
        }

        public static bool SendErrorReport
        {
            get
            {
                return (bool)GetValue("SendErrorReport", true);
            }
            set
            {
                SetValue("SendErrorReport", value);
            }
        }

        public static DateTime LastErrorTime
        {
            get
            {
                return GetValue("LastErrorTime", DateTime.MinValue);
            }
            set
            {
                SetValue("LastErrorTime", value);
            }
        }

        public static int InitialTimeout
        {
            get
            {
                return (int)GetValue(nameof(InitialTimeout), 0);
            }
            set
            {
                SetValue(nameof(InitialTimeout), value);
            }
        }

        public static MouseActions DrawingButton
        {
            get
            {
                return (MouseActions)GetValue(nameof(DrawingButton), 0);
            }
            set
            {
                SetValue(nameof(DrawingButton), (int)value);
            }
        }

        public static bool RegisterTouchPad
        {
            get
            {
                return (bool)GetValue(nameof(RegisterTouchPad), true);
            }
            set
            {
                SetValue(nameof(RegisterTouchPad), value);
            }
        }

        public static bool RegisterTouchScreen
        {
            get
            {
                return GetValue(nameof(RegisterTouchScreen), true);
            }
            set
            {
                SetValue(nameof(RegisterTouchScreen), value);
            }
        }

        public static bool IgnoreFullScreen
        {
            get
            {
                return GetValue(nameof(IgnoreFullScreen), false);
            }
            set
            {
                SetValue(nameof(IgnoreFullScreen), value);
            }
        }

        public static bool IgnoreTouchInputWhenUsingPen
        {
            get
            {
                return GetValue(nameof(IgnoreTouchInputWhenUsingPen), true);
            }
            set
            {
                SetValue(nameof(IgnoreTouchInputWhenUsingPen), value);
            }
        }

        public static DeviceStates PenGestureButton
        {
            get
            {
                return (DeviceStates)GetValue(nameof(PenGestureButton), 0);
            }
            set
            {
                SetValue(nameof(PenGestureButton), (int)value);
            }
        }

        public static bool RunAsAdmin
        {
            get
            {
                return GetValue(nameof(RunAsAdmin), false);
            }
            set
            {
                SetValue(nameof(RunAsAdmin), value);
            }
        }

        public static bool TouchpadEdgeGesturesEnabled
        {
            get { return GetValue(nameof(TouchpadEdgeGesturesEnabled), false); }
            set { SetValue(nameof(TouchpadEdgeGesturesEnabled), value); }
        }

        public static bool TouchpadEdgeConfidenceFilteringEnabled
        {
            get { return GetValue(nameof(TouchpadEdgeConfidenceFilteringEnabled), false); }
            set { SetValue(nameof(TouchpadEdgeConfidenceFilteringEnabled), value); }
        }

        public static TouchpadWindowDragMode TouchpadWindowDragMode
        {
            get
            {
                int storedMode = GetValue(nameof(TouchpadWindowDragMode), 0);
                // Value 2 was the removed free-anchor mode; keep existing users enabled on the supported mode.
                if (storedMode == (int)TouchpadWindowDragMode.ThreeFingerDrag)
                    return TouchpadWindowDragMode.ThreeFingerDrag;
                return storedMode == (int)TouchpadWindowDragMode.BottomEdgeAnchor || storedMode == 2
                    ? TouchpadWindowDragMode.BottomEdgeAnchor
                    : TouchpadWindowDragMode.Disabled;
            }
            set
            {
                int storedMode = value == TouchpadWindowDragMode.ThreeFingerDrag
                    ? (int)TouchpadWindowDragMode.ThreeFingerDrag
                    : value == TouchpadWindowDragMode.BottomEdgeAnchor ? 1 : 0;
                SetValue(nameof(TouchpadWindowDragMode), storedMode);
            }
        }

        public static TouchpadWindowDragImplementation TouchpadWindowDragImplementation
        {
            get
            {
                int storedImplementation = GetValue(nameof(TouchpadWindowDragImplementation), 0);
                if (storedImplementation == (int)TouchpadWindowDragImplementation.ThreeFingerDrag)
                    return TouchpadWindowDragImplementation.ThreeFingerDrag;
                if (storedImplementation == (int)TouchpadWindowDragImplementation.NativeMoveLoop)
                    return TouchpadWindowDragImplementation.NativeMoveLoop;
                return storedImplementation == (int)TouchpadWindowDragImplementation.SimulatedMouseDrag
                    ? TouchpadWindowDragImplementation.SimulatedMouseDrag
                    : TouchpadWindowDragImplementation.DirectSetWindowPos;
            }
            set
            {
                int storedImplementation = value == TouchpadWindowDragImplementation.ThreeFingerDrag
                    ? (int)TouchpadWindowDragImplementation.ThreeFingerDrag
                    : value == TouchpadWindowDragImplementation.NativeMoveLoop
                        ? (int)TouchpadWindowDragImplementation.NativeMoveLoop
                        : value == TouchpadWindowDragImplementation.SimulatedMouseDrag ? 1 : 0;
                SetValue(nameof(TouchpadWindowDragImplementation), storedImplementation);
            }
        }

        public static bool TouchpadWindowDragBringToFront
        {
            get { return GetValue(nameof(TouchpadWindowDragBringToFront), true); }
            set { SetValue(nameof(TouchpadWindowDragBringToFront), value); }
        }

        public static int TouchpadEdgeZonePercent
        {
            get { return GetValue(nameof(TouchpadEdgeZonePercent), 12); }
            set { SetValue(nameof(TouchpadEdgeZonePercent), Math.Max(0, Math.Min(25, value))); }
        }

        public static int TouchpadLeftEdgeZonePercent
        {
            get { return GetValue(nameof(TouchpadLeftEdgeZonePercent), TouchpadEdgeZonePercent); }
            set { SetValue(nameof(TouchpadLeftEdgeZonePercent), Math.Max(0, Math.Min(25, value))); }
        }

        public static int TouchpadRightEdgeZonePercent
        {
            get { return GetValue(nameof(TouchpadRightEdgeZonePercent), TouchpadEdgeZonePercent); }
            set { SetValue(nameof(TouchpadRightEdgeZonePercent), Math.Max(0, Math.Min(25, value))); }
        }

        public static int TouchpadTopEdgeZonePercent
        {
            get { return GetValue(nameof(TouchpadTopEdgeZonePercent), TouchpadEdgeZonePercent); }
            set { SetValue(nameof(TouchpadTopEdgeZonePercent), Math.Max(0, Math.Min(25, value))); }
        }

        public static int TouchpadBottomEdgeZonePercent
        {
            get { return GetValue(nameof(TouchpadBottomEdgeZonePercent), TouchpadEdgeZonePercent); }
            set { SetValue(nameof(TouchpadBottomEdgeZonePercent), Math.Max(0, Math.Min(25, value))); }
        }

        public static int TouchpadEdgeActivationPercent
        {
            get { return GetValue(nameof(TouchpadEdgeActivationPercent), 8); }
            set { SetValue(nameof(TouchpadEdgeActivationPercent), Math.Max(3, Math.Min(20, value))); }
        }

        public static int TouchpadWindowDragSensitivityPercent
        {
            get { return GetValue(nameof(TouchpadWindowDragSensitivityPercent), 100); }
            set { SetValue(nameof(TouchpadWindowDragSensitivityPercent), Math.Max(40, Math.Min(250, value))); }
        }

        #endregion

        static AppConfig()
        {
#if uiAccess
            UiAccess = VersionHelper.IsWindows8OrGreater();
#endif
            CurrentFolderPath = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
#if Portable
            ApplicationDataPath = Path.Combine(CurrentFolderPath, "AppData");
            LocalApplicationDataPath = ApplicationDataPath;

            try
            {
                ProductDataMigration.MigratePortableData(ApplicationDataPath);
            }
            catch
            {
            }
            string portableConfigPath = Path.Combine(ApplicationDataPath, Constants.ConfigFileName);
            string legacyPortableConfigPath = Path.Combine(ApplicationDataPath,
                ProductDataMigration.LegacyConfigFileName);
            ConfigPath = File.Exists(portableConfigPath) || !File.Exists(legacyPortableConfigPath)
                ? portableConfigPath
                : legacyPortableConfigPath;
            BackupPath = Path.Combine(LocalApplicationDataPath, "Backup");
#else
            string roamingRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string legacyApplicationDataPath = Path.Combine(roamingRoot,
                ProductDataMigration.LegacyProductName);
            string legacyLocalApplicationDataPath = Path.Combine(localRoot,
                ProductDataMigration.LegacyProductName);
            string currentApplicationDataPath = Path.Combine(roamingRoot,
                ProductDataMigration.CurrentProductName);
            string currentLocalApplicationDataPath = Path.Combine(localRoot,
                ProductDataMigration.CurrentProductName);
            bool migrationFailed = false;
            try
            {
                ProductDataMigration.MigrateInstalledData(legacyApplicationDataPath,
                    currentApplicationDataPath, legacyLocalApplicationDataPath,
                    currentLocalApplicationDataPath);
            }
            catch
            {
                migrationFailed = true;
            }

            bool useLegacyData = migrationFailed && Directory.Exists(legacyApplicationDataPath);
            ApplicationDataPath = useLegacyData ? legacyApplicationDataPath : currentApplicationDataPath;
            LocalApplicationDataPath = useLegacyData && Directory.Exists(legacyLocalApplicationDataPath)
                ? legacyLocalApplicationDataPath
                : currentLocalApplicationDataPath;
            string currentConfigPath = Path.Combine(ApplicationDataPath, Constants.ConfigFileName);
            string legacyConfigPath = Path.Combine(ApplicationDataPath,
                ProductDataMigration.LegacyConfigFileName);
            ConfigPath = File.Exists(currentConfigPath) || !File.Exists(legacyConfigPath)
                ? currentConfigPath
                : legacyConfigPath;
            BackupPath = Path.Combine(LocalApplicationDataPath, "Backup");

#endif
            ExeMap = new ExeConfigurationFileMap
            {
                ExeConfigFilename = ConfigPath,
                RoamingUserConfigFilename = ConfigPath,
            };
            Timer = new Timer(SaveFile, null, Timeout.Infinite, Timeout.Infinite);
        }

        public static void Reload()
        {
            _loadFlag = true;
            _settingCache.Clear();
            if (ConfigChanged != null)
                ConfigChanged(new object(), EventArgs.Empty);
        }


        private static void Save()
        {
            Timer.Change(100, Timeout.Infinite);
        }

        private static void SaveFile(object state)
        {
            try
            {
                FileManager.WaitFile(ConfigPath);
                // Save the configuration file.    
                var config = Config;
                config.AppSettings.SectionInformation.ForceSave = true;
                config.Save(ConfigurationSaveMode.Modified);
            }
            catch (ConfigurationErrorsException e)
            {
                Reload();
                Logging.LogAndNotice(new Exceptions.FileWriteException(e));
            }
            catch (Exception e)
            {
                Logging.LogAndNotice(e);
            }
            // Force a reload of the changed section.    
            ConfigurationManager.RefreshSection("appSettings");
            ConfigChanged?.Invoke(new object(), EventArgs.Empty);
        }

        private static T GetValue<T>(string key, T defaultValue, Func<string, T> converter)
        {
            if (Config == null)
                return defaultValue;
            var setting = Config.AppSettings.Settings[key];
            if (setting != null)
            {
                try
                {
                    return converter(setting.Value);
                }
                catch
                {
                    Config.AppSettings.Settings.Remove(key);
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        private static T GetCacheValue<T>(string key, T defaultValue, Func<string, T> converter)
        {
            object output;
            if (_settingCache.TryGetValue(key, out output))
            {
                return (T)output;
            }
            else
            {
                var value = GetValue(key, defaultValue, converter);
                _settingCache.Add(key, value);
                return value;
            }
        }

        private static int GetValue(string key, int defaultValue)
        {
            return GetCacheValue(key, defaultValue, s => int.Parse(s));
        }

        private static double GetValue(string key, double defaultValue)
        {
            return GetCacheValue(key, defaultValue, s => double.Parse(s));
        }

        private static bool GetValue(string key, bool defaultValue)
        {
            return GetCacheValue(key, defaultValue, s => bool.Parse(s));
        }

        private static string GetValue(string key, string defaultValue)
        {
            return GetValue(key, defaultValue, s => s);
        }

        private static DateTime GetValue(string key, DateTime defaultValue)
        {
            string setting = GetValue(key, string.Empty);
            if (!string.IsNullOrEmpty(setting))
            {
                try
                {
                    return DateTime.Parse(setting);
                }
                catch
                {
                    Config.AppSettings.Settings.Remove(key);
                    return defaultValue;
                }
            }
            else return defaultValue;
        }

        private static System.Drawing.Color GetValue(string key, System.Drawing.Color defaultValue)
        {
            string setting = GetValue(key, string.Empty);
            if (!string.IsNullOrEmpty(setting))
            {
                try
                {
                    return GetCacheValue(key, defaultValue, System.Drawing.ColorTranslator.FromHtml);
                }
                catch
                {
                    Config.AppSettings.Settings.Remove(key);
                    return defaultValue;
                }
            }
            else
            {
                System.Drawing.Color color;
                if (GetWindowGlassColor(out color))
                    return color;
                return defaultValue;
            }
        }

        private static void SetValue<T>(string key, T value)
        {
            if (Config == null)
                return;
            _settingCache.Clear();

            if (Config.AppSettings.Settings[key] != null)
            {
                Config.AppSettings.Settings[key].Value = value.ToString();
            }
            else
            {
                Config.AppSettings.Settings.Add(key, value.ToString());
            }
            Save();
        }

        private static void SetValue(string key, System.Drawing.Color value)
        {
            SetValue(key, System.Drawing.ColorTranslator.ToHtml(value));
        }

        private static void SetValue(string key, DateTime value)
        {
            SetValue(key, value.ToString(CultureInfo.InvariantCulture));
        }

        private static bool GetWindowGlassColor(out System.Drawing.Color windowGlassColor)
        {
            windowGlassColor = System.Drawing.Color.Empty;
            try
            {
                if (VersionHelper.IsWindowsVistaOrGreater())
                {
                    using (RegistryKey dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    {
                        if (dwm == null)
                            return false;
                        var colorizationColor = dwm.GetValue("ColorizationColor");
                        if (colorizationColor == null)
                            return false;

                        windowGlassColor = System.Drawing.Color.FromArgb((int)colorizationColor | -16777216);
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }
    }
}

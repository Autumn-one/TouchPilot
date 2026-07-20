using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Localization;
using GestureSign.ControlPanel.Common;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using ManagedWinapi.Hooks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using Color = System.Drawing.Color;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace GestureSign.ControlPanel.MainWindowControls
{
    /// <summary>
    /// Options.xaml 的交互逻辑
    /// </summary>
    public partial class Options : UserControl
    {
        Color _VisualFeedbackColor;
        private bool _loading;

        public Options()
        {
            InitializeComponent();
            if (AppConfig.UiAccess)
            {
                RunAsAdminCheckBox.ClearValue(UIElement.VisibilityProperty);
                RunAsAdminCheckBox.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadSettings()
        {
            try
            {
                // Try to load saved settings
                //  Common.Configuration.AppConfig.Reload();
                CheckStartupStatus();

                GestureTrailSwitch.IsOn = AppConfig.VisualFeedbackWidth > 0;
                _VisualFeedbackColor = AppConfig.VisualFeedbackColor;
                VisualFeedbackWidthSlider.Value = AppConfig.VisualFeedbackWidth;
                MinimumPointDistanceSlider.Value = AppConfig.MinimumPointDistance;
                OpacitySlider.Value = AppConfig.Opacity;
                ShowTrayIconSwitch.IsOn = AppConfig.ShowTrayIcon;
                SendLogToggleSwitch.IsOn = AppConfig.SendErrorReport;
                TouchPadSwitch.IsOn = AppConfig.RegisterTouchPad;
                TouchScreenSwitch.IsOn = AppConfig.RegisterTouchScreen;
                IgnoreFullScreenSwitch.IsOn = AppConfig.IgnoreFullScreen;
                IgnoreTouchInputWhenUsingPenSwitch.IsOn = AppConfig.IgnoreTouchInputWhenUsingPen;
                if (AppConfig.DrawingButton != MouseActions.None)
                {
                    MouseSwitch.IsOn = true;
                    DrawingButtonComboBox.SelectedValue = AppConfig.DrawingButton;
                }

                var languageList = LocalizationProvider.Instance.GetLanguageList("ControlPanel");
                LanguageComboBox.ItemsSource = languageList;

                string cultureName = AppConfig.CultureName;
                if (String.IsNullOrEmpty(cultureName) && languageList != null)
                {
                    CultureInfo currentCulture = CultureInfo.CurrentUICulture;
                    if (languageList.ContainsKey(currentCulture.Name))
                        cultureName = currentCulture.Name;
                    else if (languageList.ContainsKey(currentCulture.TwoLetterISOLanguageName))
                        cultureName = currentCulture.TwoLetterISOLanguageName;
                    else if (languageList.ContainsKey("en"))
                        cultureName = "en";
                }
                LanguageComboBox.SelectedValue = cultureName;
                if (AppConfig.InitialTimeout > 0)
                {
                    InitialTimeoutSwitch.IsOn = true;
                    InitialTimeoutSlider.Value = AppConfig.InitialTimeout / 1000f;
                }

                var penState = AppConfig.PenGestureButton;
                if ((penState & (DeviceStates.InRange | DeviceStates.Tip)) != 0 && (penState & (DeviceStates.RightClickButton | DeviceStates.Invert)) != 0)
                {
                    PenGestureSwitch.IsOn = true;
                    TipCheckBox.IsChecked = penState.HasFlag(DeviceStates.Tip);
                    HoverCheckBox.IsChecked = penState.HasFlag(DeviceStates.InRange);
                    RightClickButtonCheckBox.IsChecked = penState.HasFlag(DeviceStates.RightClickButton);
                    EraserCheckBox.IsChecked = penState.HasFlag(DeviceStates.Invert);
                }
                else
                {
                    PenGestureSwitch.IsOn = false;
                }
                CheckDeviceStates();
            }
            catch (Exception)
            {
                MessageBox.Show(LocalizationProvider.Instance.GetTextValue("Options.Messages.LoadSettingError"),
                    LocalizationProvider.Instance.GetTextValue("Options.Messages.LoadSettingErrorTitle"), MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UserControl_Initialized(object sender, EventArgs e)
        {
            _loading = true;
            try
            {
                LoadSettings();
            }
            finally
            {
                _loading = false;
            }

            UpdateVisualFeedbackExample();
        }

        private void SetSwitchState(ToggleSwitch toggleSwitch, bool isOn)
        {
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                toggleSwitch.IsOn = isOn;
            }
            finally
            {
                _loading = wasLoading;
            }
        }

        private void btnPickColor_Click(object sender, RoutedEventArgs e)
        {
            // Set color picker dialog color to current visual feedback color
            ColorDialog cdColorPicker = new ColorDialog();
            cdColorPicker.AllowFullOpen = true;
            cdColorPicker.Color = _VisualFeedbackColor;// System.Drawing.Color.FromArgb(_VisualFeedbackColor.A, _VisualFeedbackColor.R, _VisualFeedbackColor.G, _VisualFeedbackColor.B);

            // Show color picker dialog
            if (cdColorPicker.ShowDialog() != DialogResult.OK)
                return;
            // Change color of visual feedback and refresh example
            //_VisualFeedbackColor.A = cdColorPicker.Color.A;
            //_VisualFeedbackColor.R = cdColorPicker.Color.R;
            //_VisualFeedbackColor.B = cdColorPicker.Color.B;
            //_VisualFeedbackColor.G = cdColorPicker.Color.G;
            _VisualFeedbackColor = cdColorPicker.Color;
            AppConfig.VisualFeedbackColor = _VisualFeedbackColor;
            UpdateVisualFeedbackExample();
        }

        private void SystemColorButton_Click(object sender, RoutedEventArgs e)
        {
            AppConfig.VisualFeedbackColor = Color.Empty;
            _VisualFeedbackColor = AppConfig.VisualFeedbackColor;
            UpdateVisualFeedbackExample();
        }

        private void VisualFeedbackWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateVisualFeedbackExample();

            var newValue = (int)Math.Round(e.NewValue);
            if (newValue == AppConfig.VisualFeedbackWidth) return;

            AppConfig.VisualFeedbackWidth = newValue;
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Change opacity display text with new value
            OpacityText.Text = LocalizationProvider.Instance.GetTextValue("Options.Opacity") + GetAlphaPercentage(OpacitySlider.Value) + "%";
            if (Math.Abs(e.NewValue - AppConfig.Opacity) < 0.001) return;

            AppConfig.Opacity = OpacitySlider.Value;
        }

        private void MinimumPointDistanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var newValue = (int)Math.Round(e.NewValue);
            if (newValue == AppConfig.MinimumPointDistance || (int)e.OldValue == 0) return;
            AppConfig.MinimumPointDistance = newValue;
        }

        private int GetAlphaPercentage(double Alpha)
        {
            return (int)Math.Round(Alpha * 100d);
        }

        private void UpdateVisualFeedbackExample()
        {
            // Show new example graphic if visual feedback is enabled
            if (VisualFeedbackWidthSlider.Value > 0)
            {
                VisualFeedbackExample.Stroke = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(_VisualFeedbackColor.A, _VisualFeedbackColor.R, _VisualFeedbackColor.G, _VisualFeedbackColor.B));

                VisualFeedbackWidthText.Text = String.Format(LocalizationProvider.Instance.GetTextValue("Options.VisualFeedbackWidth"), VisualFeedbackWidthSlider.Value);
            }
            else
            {
                VisualFeedbackWidthText.Text = LocalizationProvider.Instance.GetTextValue("Options.Off");
            }

        }

        private void CheckDeviceStates()
        {
            NamedPipe.GetMessageAsync(GestureSign.Common.Constants.Daemon + "DeviceState", 2000).ContinueWith(
                t =>
                {
                    if (t.Result == null)
                        return;

                    Dispatcher.Invoke(() =>
                    {
                        Devices deviceState = (Devices)t.Result;
                        Brush brush = (Brush)TryFindResource("MahApps.Brushes.AccentBase");
                        TouchScreenNotFoundText.Foreground = (deviceState & Devices.TouchScreen) != 0 ? Brushes.Transparent : brush;
                        TouchPadNotFoundText.Foreground = (deviceState & Devices.TouchPad) != 0 ? Brushes.Transparent : brush;
                        PenNotFoundText.Foreground = (deviceState & Devices.Pen) != 0 ? Brushes.Transparent : brush;
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                });
        }

        private void CheckStartupStatus()
        {
            if (StartupHelper.IsRunAsAdmin)
            {
                SetSwitchState(StartupSwitch, true);
                RunAsAdminCheckBox.IsChecked = true;
            }
            else
            {
                RunAsAdminCheckBox.IsChecked = false;

#if ConvertedDesktopApp
                StartupHelper.CheckStoreAppStartupStatus().ContinueWith(t =>
                {
                    bool result = t.Result;
                    Dispatcher.Invoke(() =>
                    {
                        SetSwitchState(StartupSwitch, result);
                    }, System.Windows.Threading.DispatcherPriority.Background);
                });
#else
                SetSwitchState(StartupSwitch, StartupHelper.GetStartupStatus());
#endif

            }
        }

        private void EnableStartup()
        {
#if ConvertedDesktopApp
            StartupHelper.EnableStoreAppStartup().ContinueWith(t =>
            {
                if (!t.Result)
                {
                    Dispatcher.Invoke(() =>
                    {
                        SetSwitchState(StartupSwitch, false);
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            });
#else
            if (!StartupHelper.EnableNormalStartup())
            {
                SetSwitchState(StartupSwitch, false);
            }
#endif
        }

        private void DisableStartup()
        {
#if ConvertedDesktopApp
            StartupHelper.DisableStoreAppStartup().ContinueWith(t =>
            {
                if (!t.Result)
                {
                    Dispatcher.Invoke(() =>
                    {
                        SetSwitchState(StartupSwitch, true);
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            });
#else

            if (!StartupHelper.DisableNormalStartup())
            {
                SetSwitchState(StartupSwitch, true);
            }
#endif
        }

        private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            try
            {
                if (StartupSwitch.IsOn)
                {
                    EnableStartup();
                }
                else
                {
                    DisableStartup();
                    if (StartupHelper.IsRunAsAdmin)
                    {
                        if (StartupHelper.DisableHighPrivilegeStartup())
                        {
                            AppConfig.RunAsAdmin = false;
                            RunAsAdminCheckBox.IsChecked = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            { MessageBox.Show(ex.Message, LocalizationProvider.Instance.GetTextValue("Messages.Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void RunAsAdminCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (RunAsAdminCheckBox.IsChecked.GetValueOrDefault())
            {
                if (StartupHelper.EnableHighPrivilegeStartup())
                {
                    DisableStartup();
                    AppConfig.RunAsAdmin = true;
                }
                else
                {
                    RunAsAdminCheckBox.IsChecked = false;
                }
            }
            else
            {
                if (StartupHelper.DisableHighPrivilegeStartup())
                {
                    EnableStartup();
                    AppConfig.RunAsAdmin = false;
                }
                else
                {
                    RunAsAdminCheckBox.IsChecked = true;
                }
            }
        }

        private void ShowTrayIconSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.ShowTrayIcon = ShowTrayIconSwitch.IsOn;
        }

        private void SendLogToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.SendErrorReport = SendLogToggleSwitch.IsOn;
        }

        private void LanguageComboBox_DropDownClosed(object sender, EventArgs e)
        {
            if (LanguageComboBox.SelectedValue == null) return;
            AppConfig.CultureName = (string)LanguageComboBox.SelectedValue;
        }

        private void MouseSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (MouseSwitch.IsOn)
                DrawingButtonComboBox.SelectedValue = AppConfig.DrawingButton = MouseActions.Right;
            else AppConfig.DrawingButton = MouseActions.None;
        }

        private void DrawingButtonComboBox_DropDownClosed(object sender, EventArgs e)
        {
            AppConfig.DrawingButton = (MouseActions)DrawingButtonComboBox.SelectedValue;
        }

        private void TouchScreenSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.RegisterTouchScreen = TouchScreenSwitch.IsOn;
        }

        private void TouchPadSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.RegisterTouchPad = TouchPadSwitch.IsOn;
        }

        private void IgnoreFullScreenSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.IgnoreFullScreen = IgnoreFullScreenSwitch.IsOn;
        }

        private void IgnoreTouchInputWhenUsingPenSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.IgnoreTouchInputWhenUsingPen = IgnoreTouchInputWhenUsingPenSwitch.IsOn;
        }

        private void InitialTimeoutSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (InitialTimeoutSwitch.IsOn)
            {
                InitialTimeoutSlider.Value = 0.6;
            }
            else
            {
                InitialTimeoutSlider.Value = 0;
            }
        }

        private void InitialTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var newValue = (int)Math.Round(e.NewValue * 1000);
            if (newValue == AppConfig.InitialTimeout) return;
            AppConfig.InitialTimeout = newValue;
        }

        private void PenGestureSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (PenGestureSwitch.IsOn)
            {
                AppConfig.PenGestureButton = DeviceStates.RightClickButton | DeviceStates.Tip;
                RightClickButtonCheckBox.IsChecked = TipCheckBox.IsChecked = true;
                EraserCheckBox.IsChecked = HoverCheckBox.IsChecked = false;
            }
            else
            {
                AppConfig.PenGestureButton = DeviceStates.None;
                RightClickButtonCheckBox.IsChecked = EraserCheckBox.IsChecked = HoverCheckBox.IsChecked = TipCheckBox.IsChecked = false;
            }
        }

        private void RightClickButtonCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (RightClickButtonCheckBox.IsChecked.GetValueOrDefault())
            {
                AppConfig.PenGestureButton |= DeviceStates.RightClickButton;
            }
            else
            {
                AppConfig.PenGestureButton &= ~DeviceStates.RightClickButton;
            }
        }

        private void EraserCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (EraserCheckBox.IsChecked.GetValueOrDefault())
            {
                AppConfig.PenGestureButton |= DeviceStates.Invert;
            }
            else
            {
                AppConfig.PenGestureButton &= ~DeviceStates.Invert;
            }
        }

        private void TipCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (TipCheckBox.IsChecked.GetValueOrDefault())
            {
                AppConfig.PenGestureButton |= DeviceStates.Tip;
            }
            else
            {
                AppConfig.PenGestureButton &= ~DeviceStates.Tip;
            }
        }

        private void HoverCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (HoverCheckBox.IsChecked.GetValueOrDefault())
            {
                AppConfig.PenGestureButton |= DeviceStates.InRange;
            }
            else
            {
                AppConfig.PenGestureButton &= ~DeviceStates.InRange;
            }
        }

        private void GestureTrailSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (GestureTrailSwitch.IsOn)
            {
                VisualFeedbackWidthSlider.Value = 9;
            }
            else
            {
                VisualFeedbackWidthSlider.Value = 0;
            }
        }

        private void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog()
            {
                Filter = LocalizationProvider.Instance.GetTextValue("Options.BackupFile") + "|*" + GestureSign.Common.Constants.BackupFileExtension,
                FileName = LocalizationProvider.Instance.GetTextValue("Options.BackupFile") + " " + DateTime.Now.ToString("MMddHHmm"),
                Title = LocalizationProvider.Instance.GetTextValue("Options.Backup"),
                AddExtension = true,
                DefaultExt = GestureSign.Common.Constants.BackupFileExtension.Remove(0, 1),
                ValidateNames = true
            };
            if (saveFileDialog.ShowDialog().Value)
            {
                try
                {
                    Archive.CreateArchive(ApplicationManager.Instance.Applications, GestureManager.Instance.Gestures, saveFileDialog.FileName, AppConfig.ConfigPath);

                    UIHelper.GetParentWindow(this).ShowModalMessageExternal(LocalizationProvider.Instance.GetTextValue("Options.Messages.BackupCompleteTitle"), null);
                }
                catch (Exception exception)
                {
                    UIHelper.GetParentWindow(this).ShowModalMessageExternal(LocalizationProvider.Instance.GetTextValue("Messages.Error"), exception.Message);
                }
            }
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog()
            {
                Filter = $"{LocalizationProvider.Instance.GetTextValue("Options.BackupFile")}|*{GestureSign.Common.Constants.BackupFileExtension}",
                Title = LocalizationProvider.Instance.GetTextValue("Common.Import"),
                CheckFileExists = true
            };
            if (openFileDialog.ShowDialog().Value)
            {
                try
                {
                    string tempArchivePath = Archive.ExtractToTempDirectory(openFileDialog.FileName);

                    string configPath = Path.Combine(tempArchivePath, Path.GetFileName(AppConfig.ConfigPath));
                    if (File.Exists(configPath))
                        File.Copy(configPath, AppConfig.ConfigPath, true);
                    AppConfig.Reload();
                    LoadSettings();

                    var applications = FileManager.LoadObject<List<IApplication>>(Path.Combine(tempArchivePath, GestureSign.Common.Constants.ActionFileName), false, true, true);
                    var gestures = GestureManager.LoadGesturesFromFile(Path.Combine(tempArchivePath, GestureSign.Common.Constants.GesturesFileName), true);

                    if (gestures != null)
                    {
                        var oldGestures = GestureManager.Instance.Gestures;
                        foreach (var g in oldGestures)
                        {
                            GestureManager.Instance.DeleteGesture(g.Name);
                        }
                        foreach (var g in gestures)
                        {
                            GestureManager.Instance.AddGesture(g);
                        }

                        GestureManager.Instance.SaveGestures();
                    }
                    if (applications != null)
                    {
                        ApplicationManager.Instance.RemoveAllApplication();
                        ApplicationManager.Instance.AddApplicationRange(applications);

                        ApplicationManager.Instance.SaveApplications();
                    }

                    Directory.Delete(tempArchivePath, true);
                    UIHelper.GetParentWindow(this).ShowModalMessageExternal(LocalizationProvider.Instance.GetTextValue("Options.Messages.RestoreCompleteTitle"), null);
                }
                catch (Exception exception)
                {
                    UIHelper.GetParentWindow(this).ShowModalMessageExternal(LocalizationProvider.Instance.GetTextValue("Messages.Error"), exception.Message);
                }
            }
        }

        private void OpenConfigFolderButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("explorer.exe", AppConfig.ApplicationDataPath);
        }
    }
}

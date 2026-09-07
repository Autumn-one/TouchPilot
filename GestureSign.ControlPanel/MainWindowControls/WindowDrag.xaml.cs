using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.Localization;
using System;
using System.Windows;
using System.Windows.Controls;

namespace GestureSign.ControlPanel.MainWindowControls
{
    public partial class WindowDrag : UserControl
    {
        private sealed class WindowDragImplementationChoice
        {
            public TouchpadWindowDragImplementation Value { get; set; }
            public string DisplayName { get; set; }
        }

        private bool _loading = true;

        public WindowDrag()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _loading = true;
            try
            {
                WindowDragSwitch.IsOn = AppConfig.TouchpadWindowDragMode != TouchpadWindowDragMode.Disabled;
                WindowDragImplementationComboBox.ItemsSource = new[]
                {
                    new WindowDragImplementationChoice
                    {
                        Value = TouchpadWindowDragImplementation.DirectSetWindowPos,
                        DisplayName = LocalizationProvider.Instance.GetTextValue("EdgeTouch.DirectSetWindowPos")
                    },
                    new WindowDragImplementationChoice
                    {
                        Value = TouchpadWindowDragImplementation.NativeMoveLoop,
                        DisplayName = LocalizationProvider.Instance.GetTextValue("EdgeTouch.NativeMoveLoop")
                    },
                    new WindowDragImplementationChoice
                    {
                        Value = TouchpadWindowDragImplementation.SimulatedMouseDrag,
                        DisplayName = LocalizationProvider.Instance.GetTextValue("EdgeTouch.SimulatedMouseDrag")
                    },
                    new WindowDragImplementationChoice
                    {
                        Value = TouchpadWindowDragImplementation.ThreeFingerDrag,
                        DisplayName = LocalizationProvider.Instance.GetTextValue("EdgeTouch.ThreeFingerDrag")
                    },
                    new WindowDragImplementationChoice
                    {
                        Value = TouchpadWindowDragImplementation.ThreeFingerWindowDrag,
                        DisplayName = LocalizationProvider.Instance.GetTextValue("EdgeTouch.ThreeFingerWindowDrag")
                    }
                };
                WindowDragImplementationComboBox.SelectedValue = AppConfig.TouchpadWindowDragImplementation;
                UpdateWindowDragForegroundControl(AppConfig.TouchpadWindowDragImplementation);
                WindowDragSensitivitySlider.Value = AppConfig.TouchpadWindowDragSensitivityPercent;
            }
            finally
            {
                _loading = false;
            }
        }

        private void WindowDragSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading && IsLoaded)
                AppConfig.TouchpadWindowDragMode = WindowDragSwitch.IsOn
                    ? GetWindowDragMode(AppConfig.TouchpadWindowDragImplementation)
                    : TouchpadWindowDragMode.Disabled;
        }

        private void WindowDragImplementationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || WindowDragImplementationComboBox.SelectedValue == null)
                return;

            var implementation = (TouchpadWindowDragImplementation)WindowDragImplementationComboBox.SelectedValue;
            AppConfig.TouchpadWindowDragImplementation = implementation;
            if (WindowDragSwitch.IsOn)
                AppConfig.TouchpadWindowDragMode = GetWindowDragMode(implementation);
            UpdateWindowDragForegroundControl(implementation);
        }

        private void WindowDragBringToFrontSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading && IsLoaded)
                AppConfig.TouchpadWindowDragBringToFront = WindowDragBringToFrontSwitch.IsOn;
        }

        private static TouchpadWindowDragMode GetWindowDragMode(TouchpadWindowDragImplementation implementation)
        {
            if (implementation == TouchpadWindowDragImplementation.ThreeFingerWindowDrag)
                return TouchpadWindowDragMode.ThreeFingerWindowDrag;

            return implementation == TouchpadWindowDragImplementation.ThreeFingerDrag
                ? TouchpadWindowDragMode.ThreeFingerDrag
                : TouchpadWindowDragMode.BottomEdgeAnchor;
        }

        private void UpdateWindowDragForegroundControl(TouchpadWindowDragImplementation implementation)
        {
            bool forceForeground = implementation == TouchpadWindowDragImplementation.NativeMoveLoop;
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                WindowDragBringToFrontSwitch.IsOn = forceForeground || AppConfig.TouchpadWindowDragBringToFront;
                WindowDragBringToFrontSwitch.IsEnabled = !forceForeground;
            }
            finally
            {
                _loading = wasLoading;
            }
        }

        private void WindowDragSensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loading && IsLoaded)
                AppConfig.TouchpadWindowDragSensitivityPercent = (int)Math.Round(e.NewValue);
        }
    }
}

using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.Localization;
using GestureSign.ControlPanel.Visualization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GestureSign.ControlPanel.MainWindowControls
{
    public partial class EdgeTouch : UserControl
    {
        private sealed class ActionChoice
        {
            public IAction Action { get; set; }
            public string DisplayName { get; set; }
        }

        private sealed class WindowDragImplementationChoice
        {
            public TouchpadWindowDragImplementation Value { get; set; }
            public string DisplayName { get; set; }
        }

        private readonly Dictionary<ComboBox, FixedEdgeGesture> _gestureSelectors;
        private TouchpadVisualizationClient _visualizationClient;
        private bool _loading;
        private bool _subscribed;

        public EdgeTouch()
        {
            InitializeComponent();
            _gestureSelectors = new Dictionary<ComboBox, FixedEdgeGesture>
            {
                { LeftSwipeInComboBox, FixedEdgeGesture.LeftSwipeIn },
                { LeftSlideUpComboBox, FixedEdgeGesture.LeftSlideUp },
                { LeftSlideDownComboBox, FixedEdgeGesture.LeftSlideDown },
                { RightSwipeInComboBox, FixedEdgeGesture.RightSwipeIn },
                { RightSlideUpComboBox, FixedEdgeGesture.RightSlideUp },
                { RightSlideDownComboBox, FixedEdgeGesture.RightSlideDown },
                { TopSwipeInComboBox, FixedEdgeGesture.TopSwipeIn },
                { TopSlideLeftComboBox, FixedEdgeGesture.TopSlideLeft },
                { TopSlideRightComboBox, FixedEdgeGesture.TopSlideRight },
                { BottomSwipeInComboBox, FixedEdgeGesture.BottomSwipeIn },
                { BottomSlideLeftComboBox, FixedEdgeGesture.BottomSlideLeft },
                { BottomSlideRightComboBox, FixedEdgeGesture.BottomSlideRight },
                { TwoFingerLeftSwipeInComboBox, FixedEdgeGesture.TwoFingerLeftSwipeIn },
                { TwoFingerRightSwipeInComboBox, FixedEdgeGesture.TwoFingerRightSwipeIn },
                { TwoFingerTopSwipeInComboBox, FixedEdgeGesture.TwoFingerTopSwipeIn },
                { TwoFingerBottomSwipeInComboBox, FixedEdgeGesture.TwoFingerBottomSwipeIn },
                { ThreeFingerLeftSwipeInComboBox, FixedEdgeGesture.ThreeFingerLeftSwipeIn },
                { ThreeFingerRightSwipeInComboBox, FixedEdgeGesture.ThreeFingerRightSwipeIn },
                { ThreeFingerTopSwipeInComboBox, FixedEdgeGesture.ThreeFingerTopSwipeIn },
                { ThreeFingerBottomSwipeInComboBox, FixedEdgeGesture.ThreeFingerBottomSwipeIn }
            };
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeToApplicationChanges();
            if (IsVisible)
                StartTouchpadVisualization();
            await ApplicationManager.Instance.LoadingTask;
            if (!IsLoaded)
                return;

            LoadSettingsAndActions();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopTouchpadVisualization();
            if (!_subscribed)
                return;

            ApplicationManager.OnLoadApplicationsCompleted -= ApplicationManager_ApplicationsChanged;
            ApplicationManager.ApplicationSaved -= ApplicationManager_ApplicationsChanged;
            _subscribed = false;
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            if (IsVisible)
                StartTouchpadVisualization();
            else
                StopTouchpadVisualization();
        }

        private void StartTouchpadVisualization()
        {
            if (_visualizationClient != null)
                return;

            var client = new TouchpadVisualizationClient();
            client.FrameReceived += VisualizationClient_FrameReceived;
            client.ConnectionChanged += VisualizationClient_ConnectionChanged;
            _visualizationClient = client;
            client.Start();
        }

        private void StopTouchpadVisualization()
        {
            TouchpadVisualizationClient client = _visualizationClient;
            if (client == null)
                return;

            _visualizationClient = null;
            client.FrameReceived -= VisualizationClient_FrameReceived;
            client.ConnectionChanged -= VisualizationClient_ConnectionChanged;
            client.Dispose();
            TouchpadPreview.IsConnected = false;
        }

        private void VisualizationClient_FrameReceived(TouchpadVisualizationClient client,
            TouchpadVisualizationFrame frame)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (IsLoaded && ReferenceEquals(_visualizationClient, client))
                    TouchpadPreview.UpdateFrame(frame);
            }, DispatcherPriority.Render);
        }

        private void VisualizationClient_ConnectionChanged(TouchpadVisualizationClient client, bool connected)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (IsLoaded && ReferenceEquals(_visualizationClient, client))
                    TouchpadPreview.IsConnected = connected;
            }, DispatcherPriority.Render);
        }

        private void SubscribeToApplicationChanges()
        {
            if (_subscribed)
                return;

            ApplicationManager.OnLoadApplicationsCompleted += ApplicationManager_ApplicationsChanged;
            ApplicationManager.ApplicationSaved += ApplicationManager_ApplicationsChanged;
            _subscribed = true;
        }

        private void ApplicationManager_ApplicationsChanged(object sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(LoadActionChoices);
        }

        private void LoadSettingsAndActions()
        {
            _loading = true;
            try
            {
                EdgeTouchSwitch.IsOn = AppConfig.TouchpadEdgeGesturesEnabled;
                ConfidenceFilterSwitch.IsOn = AppConfig.TouchpadEdgeConfidenceFilteringEnabled;
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
                        Value = TouchpadWindowDragImplementation.SimulatedMouseDrag,
                        DisplayName = LocalizationProvider.Instance.GetTextValue("EdgeTouch.SimulatedMouseDrag")
                    },
                    new WindowDragImplementationChoice
                    {
                        Value = TouchpadWindowDragImplementation.ThreeFingerDrag,
                        DisplayName = LocalizationProvider.Instance.GetTextValue("EdgeTouch.ThreeFingerDrag")
                    }
                };
                WindowDragImplementationComboBox.SelectedValue = AppConfig.TouchpadWindowDragImplementation;
                WindowDragSensitivitySlider.Value = AppConfig.TouchpadWindowDragSensitivityPercent;
                LeftEdgeZoneSlider.Value = AppConfig.TouchpadLeftEdgeZonePercent;
                RightEdgeZoneSlider.Value = AppConfig.TouchpadRightEdgeZonePercent;
                TopEdgeZoneSlider.Value = AppConfig.TouchpadTopEdgeZonePercent;
                BottomEdgeZoneSlider.Value = AppConfig.TouchpadBottomEdgeZonePercent;
                EdgeActivationSlider.Value = AppConfig.TouchpadEdgeActivationPercent;
                LoadActionChoicesCore();
            }
            finally
            {
                _loading = false;
            }
        }

        private void LoadActionChoices()
        {
            if (!IsLoaded)
                return;

            _loading = true;
            try
            {
                LoadActionChoicesCore();
            }
            finally
            {
                _loading = false;
            }
        }

        private void LoadActionChoicesCore()
        {
            IApplication globalApplication = ApplicationManager.Instance.GetGlobalApplication();
            var choices = new List<ActionChoice>
            {
                new ActionChoice
                {
                    DisplayName = LocalizationProvider.Instance.GetTextValue("EdgeTouch.Unassigned")
                }
            };
            choices.AddRange(globalApplication.Actions
                .Where(action => action != null && action.Commands != null && action.Commands.Any())
                .OrderBy(action => action.Name)
                .Select(action => new ActionChoice
                {
                    Action = action,
                    DisplayName = string.IsNullOrWhiteSpace(action.Name)
                        ? action.Commands.First().Name
                        : action.Name
                }));

            foreach (KeyValuePair<ComboBox, FixedEdgeGesture> selector in _gestureSelectors)
            {
                selector.Key.ItemsSource = choices;
                IAction assignedAction = globalApplication.Actions.FirstOrDefault(action =>
                    action != null && action.EdgeGestures != null && action.EdgeGestures.Contains(selector.Value));
                selector.Key.SelectedItem = choices.FirstOrDefault(choice => ReferenceEquals(choice.Action, assignedAction)) ?? choices[0];
            }
        }

        private void EdgeTouchSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.TouchpadEdgeGesturesEnabled = EdgeTouchSwitch.IsOn;
        }

        private void ConfidenceFilterSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.TouchpadEdgeConfidenceFilteringEnabled = ConfidenceFilterSwitch.IsOn;
        }

        private void WindowDragSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.TouchpadWindowDragMode = WindowDragSwitch.IsOn
                    ? GetSelectedWindowDragMode()
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
        }

        private TouchpadWindowDragMode GetSelectedWindowDragMode()
        {
            if (WindowDragImplementationComboBox.SelectedValue == null)
                return TouchpadWindowDragMode.BottomEdgeAnchor;

            return GetWindowDragMode(
                (TouchpadWindowDragImplementation)WindowDragImplementationComboBox.SelectedValue);
        }

        private static TouchpadWindowDragMode GetWindowDragMode(
            TouchpadWindowDragImplementation implementation)
        {
            return implementation == TouchpadWindowDragImplementation.ThreeFingerDrag
                ? TouchpadWindowDragMode.ThreeFingerDrag
                : TouchpadWindowDragMode.BottomEdgeAnchor;
        }

        private void FixedGestureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
                return;

            var selector = sender as ComboBox;
            FixedEdgeGesture gesture;
            if (selector == null || !_gestureSelectors.TryGetValue(selector, out gesture))
                return;

            IApplication globalApplication = ApplicationManager.Instance.GetGlobalApplication();
            foreach (IAction action in globalApplication.Actions.Where(action => action != null && action.EdgeGestures != null))
                action.EdgeGestures.Remove(gesture);

            var choice = selector.SelectedItem as ActionChoice;
            if (choice?.Action != null && !choice.Action.EdgeGestures.Contains(gesture))
                choice.Action.EdgeGestures.Add(gesture);

            ApplicationManager.Instance.SaveApplications();
        }

        private void WindowDragSensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loading && IsLoaded)
                AppConfig.TouchpadWindowDragSensitivityPercent = (int)Math.Round(e.NewValue);
        }

        private void EdgeZoneSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || !IsLoaded)
                return;

            int value = (int)Math.Round(e.NewValue);
            if (ReferenceEquals(sender, LeftEdgeZoneSlider))
                AppConfig.TouchpadLeftEdgeZonePercent = value;
            else if (ReferenceEquals(sender, RightEdgeZoneSlider))
                AppConfig.TouchpadRightEdgeZonePercent = value;
            else if (ReferenceEquals(sender, TopEdgeZoneSlider))
                AppConfig.TouchpadTopEdgeZonePercent = value;
            else if (ReferenceEquals(sender, BottomEdgeZoneSlider))
                AppConfig.TouchpadBottomEdgeZonePercent = value;
        }

        private void EdgeActivationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loading && IsLoaded)
                AppConfig.TouchpadEdgeActivationPercent = (int)Math.Round(e.NewValue);
        }

    }
}

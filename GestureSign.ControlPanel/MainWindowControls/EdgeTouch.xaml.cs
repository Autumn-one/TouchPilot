using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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
                { BottomSlideRightComboBox, FixedEdgeGesture.BottomSlideRight }
            };
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeToApplicationChanges();
            await ApplicationManager.Instance.LoadingTask;
            if (!IsLoaded)
                return;

            LoadSettingsAndActions();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_subscribed)
                return;

            ApplicationManager.OnLoadApplicationsCompleted -= ApplicationManager_ApplicationsChanged;
            ApplicationManager.ApplicationSaved -= ApplicationManager_ApplicationsChanged;
            _subscribed = false;
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
                EdgeTouchSwitch.IsChecked = AppConfig.TouchpadEdgeGesturesEnabled;
                WindowDragSwitch.IsChecked = AppConfig.TouchpadWindowDragMode == TouchpadWindowDragMode.BottomEdgeAnchor;
                WindowDragImplementationComboBox.ItemsSource = new[]
                {
                    new WindowDragImplementationChoice
                    {
                        Value = TouchpadWindowDragImplementation.DirectSetWindowPos,
                        DisplayName = LocalizationProvider.Instance.GetTextValue("EdgeTouch.DirectSetWindowPos")
                    },
                    new WindowDragImplementationChoice
                    {
                        Value = TouchpadWindowDragImplementation.SimulatedCaptionDrag,
                        DisplayName = LocalizationProvider.Instance.GetTextValue("EdgeTouch.SimulatedCaptionDrag")
                    }
                };
                WindowDragImplementationComboBox.SelectedValue = AppConfig.TouchpadWindowDragImplementation;
                WindowDragSensitivitySlider.Value = AppConfig.TouchpadWindowDragSensitivityPercent;
                EdgeZoneSlider.Value = AppConfig.TouchpadEdgeZonePercent;
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

        private void EdgeTouchSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.TouchpadEdgeGesturesEnabled = EdgeTouchSwitch.IsChecked.GetValueOrDefault();
        }

        private void WindowDragSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                AppConfig.TouchpadWindowDragMode = WindowDragSwitch.IsChecked.GetValueOrDefault()
                    ? TouchpadWindowDragMode.BottomEdgeAnchor
                    : TouchpadWindowDragMode.Disabled;
        }

        private void WindowDragImplementationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || WindowDragImplementationComboBox.SelectedValue == null)
                return;

            AppConfig.TouchpadWindowDragImplementation =
                (TouchpadWindowDragImplementation)WindowDragImplementationComboBox.SelectedValue;
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
            if (!_loading && IsLoaded)
                AppConfig.TouchpadEdgeZonePercent = (int)Math.Round(e.NewValue);
        }

        private void EdgeActivationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loading && IsLoaded)
                AppConfig.TouchpadEdgeActivationPercent = (int)Math.Round(e.NewValue);
        }

    }
}

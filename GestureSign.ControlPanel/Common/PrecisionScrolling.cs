using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GestureSign.ControlPanel.Common
{
    public static class PrecisionScrolling
    {
        private const double WheelDeltaPerNotch = 120d;
        private const double OffsetTolerance = 0.01;
        private const double HighResolutionSequenceMilliseconds = 160d;

        private static readonly ConditionalWeakTable<ScrollViewer, InputState> InputStates =
            new ConditionalWeakTable<ScrollViewer, InputState>();
        private static readonly ConditionalWeakTable<ComboBox, ComboBoxState> ComboBoxStates =
            new ConditionalWeakTable<ComboBox, ComboBoxState>();
        private static readonly Dictionary<ScrollViewer, AnimationState> ActiveAnimations =
            new Dictionary<ScrollViewer, AnimationState>();

        private static bool _renderingSubscribed;

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(PrecisionScrolling),
            new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty PixelsPerNotchProperty = DependencyProperty.RegisterAttached(
            "PixelsPerNotch",
            typeof(double),
            typeof(PrecisionScrolling),
            new FrameworkPropertyMetadata(48d, FrameworkPropertyMetadataOptions.Inherits),
            IsValidNonNegativeFiniteDouble);

        public static readonly DependencyProperty DiscreteAnimationMillisecondsProperty =
            DependencyProperty.RegisterAttached(
                "DiscreteAnimationMilliseconds",
                typeof(double),
                typeof(PrecisionScrolling),
                new FrameworkPropertyMetadata(80d, FrameworkPropertyMetadataOptions.Inherits),
                IsValidNonNegativeFiniteDouble);

        public static bool GetIsEnabled(DependencyObject target)
        {
            return (bool)target.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject target, bool value)
        {
            target.SetValue(IsEnabledProperty, value);
        }

        public static double GetPixelsPerNotch(DependencyObject target)
        {
            return (double)target.GetValue(PixelsPerNotchProperty);
        }

        public static void SetPixelsPerNotch(DependencyObject target, double value)
        {
            target.SetValue(PixelsPerNotchProperty, value);
        }

        public static double GetDiscreteAnimationMilliseconds(DependencyObject target)
        {
            return (double)target.GetValue(DiscreteAnimationMillisecondsProperty);
        }

        public static void SetDiscreteAnimationMilliseconds(DependencyObject target, double value)
        {
            target.SetValue(DiscreteAnimationMillisecondsProperty, value);
        }

        internal static double CalculatePixelDelta(int wheelDelta, double pixelsPerNotch)
        {
            return -wheelDelta / WheelDeltaPerNotch * pixelsPerNotch;
        }

        internal static bool IsHighResolutionWheelDelta(int wheelDelta)
        {
            return wheelDelta % (int)WheelDeltaPerNotch != 0;
        }

        internal static double CalculateAnimatedTarget(double currentOffset,
            double previousTargetOffset,
            double pixelDelta,
            bool continuesInSameDirection,
            double scrollableHeight)
        {
            double baseOffset = continuesInSameDirection ? previousTargetOffset : currentOffset;
            return ClampOffset(baseOffset + pixelDelta, scrollableHeight);
        }

        internal static double ClampOffset(double offset, double scrollableHeight)
        {
            return Math.Max(0, Math.Min(offset, scrollableHeight));
        }

        internal static bool CanUsePixelOffsets(bool canContentScroll,
            ScrollUnit? ownerScrollUnit,
            bool hostUsesPixelOffsets = false)
        {
            return !canContentScroll ||
                   ownerScrollUnit == ScrollUnit.Pixel ||
                   hostUsesPixelOffsets;
        }

        private static bool IsValidNonNegativeFiniteDouble(object value)
        {
            return value is double number && number >= 0 && !double.IsNaN(number) && !double.IsInfinity(number);
        }

        private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            var element = target as UIElement;
            if (element == null)
                return;

            if ((bool)e.NewValue)
            {
                element.PreviewMouseWheel += OnPreviewMouseWheel;
                element.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
                element.PreviewKeyDown += OnPreviewKeyDown;
                if (element is FrameworkElement frameworkElement)
                    frameworkElement.Unloaded += OnHostUnloaded;
                if (element is ComboBox comboBox)
                {
                    comboBox.DropDownOpened += OnComboBoxDropDownOpened;
                    if (comboBox.IsDropDownOpen)
                        AttachComboBoxDropDown(comboBox);
                }
            }
            else
            {
                element.PreviewMouseWheel -= OnPreviewMouseWheel;
                element.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
                element.PreviewKeyDown -= OnPreviewKeyDown;
                if (element is FrameworkElement frameworkElement)
                    frameworkElement.Unloaded -= OnHostUnloaded;
                if (element is ComboBox comboBox)
                {
                    comboBox.DropDownOpened -= OnComboBoxDropDownOpened;
                    DetachComboBoxDropDown(comboBox);
                }
                CancelAnimationsWithin(element);
            }
        }

        private static void OnComboBoxDropDownOpened(object sender, EventArgs e)
        {
            AttachComboBoxDropDown((ComboBox)sender);
        }

        private static void AttachComboBoxDropDown(ComboBox comboBox)
        {
            comboBox.ApplyTemplate();
            ScrollViewer scrollViewer = FindComboBoxDropDownScrollViewer(comboBox);
            if (scrollViewer == null)
                return;

            ComboBoxState state = ComboBoxStates.GetOrCreateValue(comboBox);
            if (state.DropDownScrollViewer != null &&
                !ReferenceEquals(state.DropDownScrollViewer, scrollViewer))
            {
                SetIsEnabled(state.DropDownScrollViewer, false);
            }

            state.DropDownScrollViewer = scrollViewer;
            SetPixelsPerNotch(scrollViewer, GetPixelsPerNotch(comboBox));
            SetDiscreteAnimationMilliseconds(scrollViewer,
                GetDiscreteAnimationMilliseconds(comboBox));
            SetIsEnabled(scrollViewer, true);
        }

        private static void DetachComboBoxDropDown(ComboBox comboBox)
        {
            if (!ComboBoxStates.TryGetValue(comboBox, out ComboBoxState state))
                return;

            if (state.DropDownScrollViewer != null)
                SetIsEnabled(state.DropDownScrollViewer, false);
            ComboBoxStates.Remove(comboBox);
        }

        internal static ScrollViewer FindComboBoxDropDownScrollViewer(ComboBox comboBox)
        {
            var popup = comboBox?.Template?.FindName("PART_Popup", comboBox) as Popup;
            return FindVisualDescendant<ScrollViewer>(popup?.Child);
        }

        private static T FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
                return null;
            if (root is T result)
                return result;

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                result = FindVisualDescendant<T>(VisualTreeHelper.GetChild(root, index));
                if (result != null)
                    return result;
            }

            return null;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || e.Delta == 0)
                return;

            var host = (UIElement)sender;
            double pixelDelta = CalculatePixelDelta(e.Delta, GetPixelsPerNotch(host));
            ScrollViewer scrollViewer = FindScrollableViewer(host, e.OriginalSource as DependencyObject, pixelDelta);
            if (scrollViewer == null)
                return;

            long timestamp = Stopwatch.GetTimestamp();
            InputState inputState = InputStates.GetOrCreateValue(scrollViewer);
            bool highResolutionDelta = IsHighResolutionWheelDelta(e.Delta);
            if (highResolutionDelta)
            {
                inputState.HighResolutionUntilTimestamp = timestamp +
                    MillisecondsToStopwatchTicks(HighResolutionSequenceMilliseconds);
            }

            bool scrollImmediately = highResolutionDelta ||
                                     timestamp <= inputState.HighResolutionUntilTimestamp;
            bool moved = scrollImmediately
                ? ScrollImmediately(scrollViewer, pixelDelta)
                : StartAnimatedScroll(scrollViewer, pixelDelta,
                    GetDiscreteAnimationMilliseconds(host), timestamp);
            if (moved)
                e.Handled = true;
        }

        private static ScrollViewer FindScrollableViewer(UIElement host,
            DependencyObject originalSource,
            double pixelDelta)
        {
            DependencyObject current = originalSource;
            while (current != null)
            {
                if (current is ScrollViewer scrollViewer &&
                    UsesPixelOffsets(scrollViewer, host) &&
                    CanScroll(scrollViewer, pixelDelta))
                {
                    return scrollViewer;
                }

                if (ReferenceEquals(current, host))
                    break;
                current = GetParent(current);
            }

            return null;
        }

        private static bool UsesPixelOffsets(ScrollViewer scrollViewer, UIElement host)
        {
            if (!scrollViewer.CanContentScroll)
                return true;

            ItemsControl owner = FindItemsControlOwner(scrollViewer, host);
            ScrollUnit? scrollUnit = owner == null
                ? null
                : VirtualizingPanel.GetScrollUnit(owner);
            return CanUsePixelOffsets(scrollViewer.CanContentScroll,
                scrollUnit,
                host is TextBoxBase);
        }

        private static ItemsControl FindItemsControlOwner(ScrollViewer scrollViewer, UIElement host)
        {
            if (scrollViewer.TemplatedParent is ItemsControl templatedOwner)
                return templatedOwner;

            DependencyObject current = scrollViewer;
            while (current != null)
            {
                if (current is ItemsControl owner)
                    return owner;
                if (ReferenceEquals(current, host))
                    break;
                current = GetParent(current);
            }

            return null;
        }

        private static DependencyObject GetParent(DependencyObject target)
        {
            if (target is Visual || target is System.Windows.Media.Media3D.Visual3D)
                return VisualTreeHelper.GetParent(target);

            if (target is ContentElement contentElement)
            {
                DependencyObject parent = ContentOperations.GetParent(contentElement);
                if (parent != null)
                    return parent;
                if (contentElement is FrameworkContentElement frameworkContentElement)
                    return frameworkContentElement.Parent;
            }

            return LogicalTreeHelper.GetParent(target);
        }

        private static bool CanScroll(ScrollViewer scrollViewer, double pixelDelta)
        {
            if (scrollViewer.ScrollableHeight <= OffsetTolerance)
                return false;

            return pixelDelta > 0
                ? scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - OffsetTolerance
                : scrollViewer.VerticalOffset > OffsetTolerance;
        }

        private static bool ScrollImmediately(ScrollViewer scrollViewer, double pixelDelta)
        {
            CancelAnimation(scrollViewer);
            double targetOffset = ClampOffset(scrollViewer.VerticalOffset + pixelDelta,
                scrollViewer.ScrollableHeight);
            if (Math.Abs(targetOffset - scrollViewer.VerticalOffset) <= OffsetTolerance)
                return false;

            scrollViewer.ScrollToVerticalOffset(targetOffset);
            return true;
        }

        private static bool StartAnimatedScroll(ScrollViewer scrollViewer,
            double pixelDelta,
            double durationMilliseconds,
            long timestamp)
        {
            if (durationMilliseconds <= 0)
                return ScrollImmediately(scrollViewer, pixelDelta);

            double currentOffset = scrollViewer.VerticalOffset;
            int direction = Math.Sign(pixelDelta);
            bool continuesInSameDirection = ActiveAnimations.TryGetValue(scrollViewer, out AnimationState current) &&
                                            current.Direction == direction;
            double previousTarget = continuesInSameDirection ? current.TargetOffset : currentOffset;
            double targetOffset = CalculateAnimatedTarget(currentOffset,
                previousTarget,
                pixelDelta,
                continuesInSameDirection,
                scrollViewer.ScrollableHeight);
            if (Math.Abs(targetOffset - currentOffset) <= OffsetTolerance)
                return false;

            ActiveAnimations[scrollViewer] = new AnimationState
            {
                StartOffset = currentOffset,
                TargetOffset = targetOffset,
                StartTimestamp = timestamp,
                DurationMilliseconds = durationMilliseconds,
                Direction = direction
            };
            SubscribeToRendering();
            return true;
        }

        private static void SubscribeToRendering()
        {
            if (_renderingSubscribed)
                return;

            CompositionTarget.Rendering += OnRendering;
            _renderingSubscribed = true;
        }

        private static void OnRendering(object sender, EventArgs e)
        {
            long timestamp = Stopwatch.GetTimestamp();
            foreach (var pair in ActiveAnimations.ToArray())
            {
                ScrollViewer scrollViewer = pair.Key;
                AnimationState animation = pair.Value;
                if (!scrollViewer.IsLoaded)
                {
                    ActiveAnimations.Remove(scrollViewer);
                    continue;
                }

                double elapsedMilliseconds = StopwatchTicksToMilliseconds(timestamp - animation.StartTimestamp);
                double progress = Math.Min(1, elapsedMilliseconds / animation.DurationMilliseconds);
                double easedProgress = 1 - Math.Pow(1 - progress, 3);
                double targetOffset = ClampOffset(animation.TargetOffset, scrollViewer.ScrollableHeight);
                double offset = animation.StartOffset + (targetOffset - animation.StartOffset) * easedProgress;
                scrollViewer.ScrollToVerticalOffset(offset);

                if (progress >= 1)
                    ActiveAnimations.Remove(scrollViewer);
            }

            if (ActiveAnimations.Count == 0 && _renderingSubscribed)
            {
                CompositionTarget.Rendering -= OnRendering;
                _renderingSubscribed = false;
            }
        }

        private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CancelAnimationsWithin((DependencyObject)sender);
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            CancelAnimationsWithin((DependencyObject)sender);
        }

        private static void OnHostUnloaded(object sender, RoutedEventArgs e)
        {
            CancelAnimationsWithin((DependencyObject)sender);
        }

        private static void CancelAnimationsWithin(DependencyObject host)
        {
            foreach (ScrollViewer scrollViewer in ActiveAnimations.Keys.ToArray())
            {
                if (ReferenceEquals(scrollViewer, host) || IsDescendantOf(scrollViewer, host))
                    ActiveAnimations.Remove(scrollViewer);
            }

            if (ActiveAnimations.Count == 0 && _renderingSubscribed)
            {
                CompositionTarget.Rendering -= OnRendering;
                _renderingSubscribed = false;
            }
        }

        private static bool IsDescendantOf(DependencyObject target, DependencyObject ancestor)
        {
            DependencyObject current = target;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
                current = GetParent(current);
            }

            return false;
        }

        private static void CancelAnimation(ScrollViewer scrollViewer)
        {
            ActiveAnimations.Remove(scrollViewer);
            if (ActiveAnimations.Count == 0 && _renderingSubscribed)
            {
                CompositionTarget.Rendering -= OnRendering;
                _renderingSubscribed = false;
            }
        }

        private static long MillisecondsToStopwatchTicks(double milliseconds)
        {
            return (long)(milliseconds * Stopwatch.Frequency / 1000d);
        }

        private static double StopwatchTicksToMilliseconds(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
        }

        private sealed class InputState
        {
            public long HighResolutionUntilTimestamp { get; set; }
        }

        private sealed class ComboBoxState
        {
            public ScrollViewer DropDownScrollViewer { get; set; }
        }

        private sealed class AnimationState
        {
            public double StartOffset { get; set; }
            public double TargetOffset { get; set; }
            public long StartTimestamp { get; set; }
            public double DurationMilliseconds { get; set; }
            public int Direction { get; set; }
        }
    }
}

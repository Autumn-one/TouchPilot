using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GestureSign.ControlPanel.Common
{
    /// <summary>
    /// http://matthamilton.net/touchscrolling-for-scrollviewer
    /// </summary>
    public class DragScrolling : DependencyObject
    {
        private static double _verticalOffset;
        private static Point _downPoint;

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        public bool IsEnabled
        {
            get { return (bool)GetValue(IsEnabledProperty); }
            set { SetValue(IsEnabledProperty, value); }
        }

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(DragScrolling), new UIPropertyMetadata(false, IsEnabledChanged));

        private static void IsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var target = d as ScrollViewer;
            if (target == null) return;

            if ((bool)e.NewValue)
            {
                UnregisterEvents(target);
                RegisterEvents(target);
            }
        }

        static void RegisterEvents(FrameworkElement target)
        {
            target.PreviewMouseLeftButtonDown += target_PreviewMouseLeftButtonDown;
            target.PreviewMouseMove += target_PreviewMouseMove;
            target.PreviewMouseLeftButtonUp += target_PreviewMouseLeftButtonUp;
        }

        static void UnregisterEvents(FrameworkElement target)
        {
            target.PreviewMouseLeftButtonDown -= target_PreviewMouseLeftButtonDown;
            target.PreviewMouseMove -= target_PreviewMouseMove;
            target.PreviewMouseLeftButtonUp -= target_PreviewMouseLeftButtonUp;
        }

        static void target_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var target = sender as ScrollViewer;
            if (target == null) return;
            if (IsScrollBarInteraction(e.OriginalSource as DependencyObject, target)) return;
            _verticalOffset = target.VerticalOffset;
            _downPoint = e.GetPosition(target);
            target.CaptureMouse();
        }

        static void target_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var target = sender as ScrollViewer;
            if (target == null || !target.IsMouseCaptured) return;

            if (Math.Abs(e.GetPosition(target).Y - _downPoint.Y) > 20)
            {
                e.Handled = true;
            }
            target.ReleaseMouseCapture();
        }

        static void target_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var target = sender as ScrollViewer;
            if (target == null || !target.IsMouseCaptured) return;

            var point = e.GetPosition(target);

            var dy = point.Y - _downPoint.Y;
            target.ScrollToVerticalOffset(_verticalOffset - dy);
        }

        internal static bool IsScrollBarInteraction(DependencyObject source, DependencyObject boundary)
        {
            DependencyObject current = source;
            while (current != null && !ReferenceEquals(current, boundary))
            {
                if (current is ScrollBar)
                    return true;
                current = GetParent(current);
            }

            return false;
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
    }
}

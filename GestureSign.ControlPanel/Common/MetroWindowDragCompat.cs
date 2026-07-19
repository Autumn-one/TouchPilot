using MahApps.Metro.Controls;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GestureSign.ControlPanel.Common
{
    internal static class MetroWindowDragCompat
    {
        private const string WindowTitleThumbName = "PART_WindowTitleThumb";
        private const string FlyoutDragThumbName = "PART_FlyoutModalDragMoveThumb";
        private static readonly DependencyProperty UseCompatibleDragProperty =
            DependencyProperty.RegisterAttached(
                "UseCompatibleDrag",
                typeof(bool),
                typeof(MetroWindowDragCompat),
                new PropertyMetadata(false));
        private static bool _initialized;

        internal static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            EventManager.RegisterClassHandler(
                typeof(MetroWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnMetroWindowLoaded));

            var handler = new MouseButtonEventHandler(OnTitleBarPreviewMouseLeftButtonDown);
            EventManager.RegisterClassHandler(
                typeof(MetroThumbContentControl),
                UIElement.PreviewMouseLeftButtonDownEvent,
                handler);
            EventManager.RegisterClassHandler(
                typeof(Thumb),
                UIElement.PreviewMouseLeftButtonDownEvent,
                handler);
        }

        private static void OnMetroWindowLoaded(object sender, RoutedEventArgs e)
        {
            var window = sender as MetroWindow;
            if (window == null || !window.IsWindowDraggable)
                return;

            window.SetValue(UseCompatibleDragProperty, true);
            window.IsWindowDraggable = false;
        }

        private static void OnTitleBarPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var thumb = sender as FrameworkElement;
            if (thumb == null || e.ClickCount != 1)
                return;

            var window = Window.GetWindow(thumb) as MetroWindow;
            if (window == null || !(bool)window.GetValue(UseCompatibleDragProperty))
                return;

            if (thumb is Thumb &&
                thumb.Name != WindowTitleThumbName && thumb.Name != FlyoutDragThumbName)
                return;

            var mousePosition = e.GetPosition(window);
            if (window.TitlebarHeight <= 0 || mousePosition.Y > window.TitlebarHeight)
                return;

            // MahApps 1.4.3 uses an internal WPF handle that is unavailable on .NET 10.
            e.Handled = true;
            window.DragMove();
        }
    }
}

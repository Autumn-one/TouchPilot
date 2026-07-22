using GestureSign.ControlPanel.Common;
using GestureSign.ControlPanel.Dialogs;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace GestureSign.Tests
{
    public class CommandDialogScrollingTests
    {
        [Fact]
        public void CommandDropDownUsesNativePixelPrecisionScrolling()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                GestureSign.ControlPanel.App app = null;
                CommandDialog dialog = null;
                try
                {
                    Assert.Null(Application.Current);
                    app = new GestureSign.ControlPanel.App
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };
                    app.InitializeComponent();

                    dialog = (CommandDialog)Activator.CreateInstance(
                        typeof(CommandDialog),
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        binder: null,
                        args: null,
                        culture: null);
                    dialog.Show();

                    var comboBox = Assert.IsType<ComboBox>(dialog.FindName("cmbPlugins"));
                    comboBox.SelectedIndex = -1;
                    comboBox.ItemsSource = Enumerable.Range(1, 80)
                        .Select(index => new DropDownItem { DisplayText = $"Command {index}" })
                        .ToArray();
                    comboBox.ApplyTemplate();
                    comboBox.IsDropDownOpen = true;
                    dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                    dialog.UpdateLayout();

                    var popup = Assert.IsType<Popup>(comboBox.Template.FindName("PART_Popup", comboBox));
                    Assert.True(popup.IsOpen);
                    var scrollViewer = Assert.IsType<ScrollViewer>(FindVisualChild<ScrollViewer>(popup.Child));

                    Assert.False(DragScrolling.GetIsEnabled(scrollViewer));
                    Assert.True(PrecisionScrolling.GetIsEnabled(scrollViewer));
                    Assert.False(scrollViewer.CanContentScroll);
                    Assert.True(scrollViewer.ScrollableHeight > 0);

                    PrecisionScrolling.SetDiscreteAnimationMilliseconds(scrollViewer, 0);
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.ScrollableHeight / 2);
                    FlushPopupLayout(dialog.Dispatcher, popup.Child, scrollViewer);
                    double middleOffset = scrollViewer.VerticalOffset;

                    RaiseWheel(scrollViewer, -120);
                    FlushPopupLayout(dialog.Dispatcher, popup.Child, scrollViewer);
                    double downOffset = scrollViewer.VerticalOffset;
                    Assert.True(downOffset > middleOffset,
                        $"Expected wheel-down to increase the offset, but it changed from {middleOffset} to {downOffset}.");

                    RaiseWheel(scrollViewer, 120);
                    FlushPopupLayout(dialog.Dispatcher, popup.Child, scrollViewer);
                    Assert.True(scrollViewer.VerticalOffset < downOffset,
                        $"Expected wheel-up to decrease the offset, but it changed from {downOffset} to {scrollViewer.VerticalOffset}.");
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    if (dialog != null)
                        dialog.Close();
                    if (app != null)
                        app.Shutdown();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The command drop-down scrolling test timed out.");
            if (failure != null)
                throw failure;
        }

        private static void RaiseWheel(UIElement target, int delta)
        {
            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = Mouse.PreviewMouseWheelEvent
            };
            target.RaiseEvent(args);
        }

        private static void FlushPopupLayout(Dispatcher dispatcher,
            UIElement popupChild,
            UIElement scrollViewer)
        {
            scrollViewer.UpdateLayout();
            popupChild.UpdateLayout();
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            scrollViewer.UpdateLayout();
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T result)
                    return result;

                result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        private sealed class DropDownItem
        {
            public string DisplayText { get; set; }
        }
    }
}

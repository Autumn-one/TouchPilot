using GestureSign.ControlPanel.Common;
using GestureSign.ControlPanel.Dialogs;
using GestureSign.Common.Localization;
using GestureSign.CorePlugins.OpenFile;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace GestureSign.Tests
{
    [Collection(DesktopInputIntegrationCollection.Name)]
    public class CommandDialogScrollingTests
    {
        [Fact]
        public void CommandAndHotKeyDropDownsUseSharedPrecisionScrolling()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                Application app = null;
                CommandDialog dialog = null;
                try
                {
                    Assert.Null(Application.Current);
                    app = new Application
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };
                    LoadApplicationResources(app);

                    var edgeTouch = new GestureSign.ControlPanel.MainWindowControls.EdgeTouch();
                    var edgeActionComboBox = Assert.IsType<ComboBox>(
                        edgeTouch.FindName("LeftSwipeInComboBox"));
                    Assert.True(PrecisionScrolling.GetIsEnabled(edgeActionComboBox));
                    Assert.Equal(ScrollUnit.Pixel,
                        VirtualizingPanel.GetScrollUnit(edgeActionComboBox));

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
                    AssertDropDownScrolling(dialog.Dispatcher, comboBox);
                    comboBox.IsDropDownOpen = false;

                    var settingsContent = Assert.IsType<ContentControl>(dialog.FindName("SettingsContent"));
                    AssertOpenFilePermissions(dialog, settingsContent);
                    var hotKeyControl = new GestureSign.CorePlugins.HotKey.HotKey();
                    settingsContent.Content = hotKeyControl;
                    settingsContent.Height = 220;
                    settingsContent.Visibility = Visibility.Visible;
                    dialog.UpdateLayout();

                    var extraKeysComboBox = Assert.IsType<ComboBox>(
                        hotKeyControl.FindName("ExtraKeysComboBox"));
                    extraKeysComboBox.MaxDropDownHeight = 180;
                    Assert.True(extraKeysComboBox.Items.Count > 0);
                    AssertDropDownScrolling(dialog.Dispatcher, extraKeysComboBox);
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

        private static void AssertOpenFilePermissions(CommandDialog dialog, ContentControl settingsContent)
        {
            LocalizationProvider.Instance.AddAssembly(typeof(OpenFilePlugin).Assembly.FullName);
            var plugin = new OpenFilePlugin();
            Assert.True(plugin.Deserialize("{\"Path\":\"example.txt\",\"Variables\":\"two words\"}"));
            var control = Assert.IsType<OpenFileControl>(plugin.GUI);
            settingsContent.Content = control;
            settingsContent.Height = double.NaN;
            settingsContent.Visibility = Visibility.Visible;
            dialog.UpdateLayout();
            dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);

            var permissions = Assert.IsType<ComboBox>(control.FindName("PermissionsComboBox"));
            Assert.Equal(0, permissions.SelectedIndex);
            Assert.Equal(2, permissions.Items.Count);
            Assert.True(PrecisionScrolling.GetIsEnabled(permissions));
            foreach (ComboBoxItem item in permissions.Items)
                Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(item.Content)));

            permissions.SelectedIndex = 1;
            var saved = JObject.Parse(plugin.Serialize());
            Assert.True(saved.Value<bool>("RunAsAdministrator"));
            Assert.Equal("example.txt", saved.Value<string>("Path"));
            Assert.Equal("two words", saved.Value<string>("Variables"));

            settingsContent.Content = null;
            Assert.True(plugin.Deserialize(saved.ToString()));
            settingsContent.Content = control;
            dialog.UpdateLayout();
            dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Assert.Equal(1, permissions.SelectedIndex);
            permissions.SelectedIndex = 0;
            Assert.False(JObject.Parse(plugin.Serialize()).Value<bool>("RunAsAdministrator"));
            dialog.UpdateLayout();
            Assert.True(permissions.TranslatePoint(new Point(), control).Y + permissions.ActualHeight <=
                control.ActualHeight + 1, "The permission selector is clipped.");

            string screenshotDirectory = Environment.GetEnvironmentVariable("TOUCHPILOT_TEST_SCREENSHOT_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
                var surface = Assert.IsAssignableFrom<FrameworkElement>(settingsContent.Parent);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
                    (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                    drawing.DrawRectangle(new VisualBrush(surface), null,
                        new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(screenshotDirectory, "open-file-permissions.png"));
                encoder.Save(output);
            }
        }

        private static void LoadApplicationResources(Application app)
        {
            app.Resources["DefaultFlowDirection"] = FlowDirection.LeftToRight;
            string[] resourceUris =
            {
                "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml",
                "pack://application:,,,/TouchPilot.ControlPanel;component/Themes/V2/Tokens.xaml",
                "pack://application:,,,/TouchPilot.ControlPanel;component/Themes/V2/Typography.xaml",
                "pack://application:,,,/TouchPilot.ControlPanel;component/Themes/V2/Controls.xaml",
                "pack://application:,,,/TouchPilot.ControlPanel;component/Themes/V2/Shell.xaml",
                "pack://application:,,,/TouchPilot.ControlPanel;component/Themes/V2/Settings.xaml",
                "pack://application:,,,/TouchPilot.ControlPanel;component/Themes/V2/Dialogs.xaml"
            };

            foreach (string resourceUri in resourceUris)
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(resourceUri, UriKind.Absolute)
                });
            }
        }

        private static void AssertDropDownScrolling(Dispatcher dispatcher, ComboBox comboBox)
        {
            comboBox.ApplyTemplate();
            comboBox.IsDropDownOpen = true;
            dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);

            var popup = Assert.IsType<Popup>(comboBox.Template.FindName("PART_Popup", comboBox));
            Assert.True(popup.IsOpen);
            popup.Child.UpdateLayout();
            var scrollViewer = Assert.IsType<ScrollViewer>(
                PrecisionScrolling.FindComboBoxDropDownScrollViewer(comboBox));

            Assert.True(PrecisionScrolling.GetIsEnabled(comboBox));
            Assert.True(PrecisionScrolling.GetIsEnabled(scrollViewer));
            Assert.True(DragScrolling.GetIsEnabled(scrollViewer));
            Assert.True(VirtualizingPanel.GetIsVirtualizing(comboBox));
            Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(comboBox));
            Assert.True(scrollViewer.CanContentScroll);
            Assert.True(scrollViewer.ScrollableHeight > 0);

            var verticalScrollBar = Assert.IsType<ScrollBar>(FindVisualChild<ScrollBar>(
                scrollViewer,
                scrollBar => scrollBar.Orientation == Orientation.Vertical));
            verticalScrollBar.ApplyTemplate();
            verticalScrollBar.UpdateLayout();
            var thumb = Assert.IsAssignableFrom<Thumb>(FindVisualChild<Thumb>(verticalScrollBar));
            Assert.True(DragScrolling.IsScrollBarInteraction(thumb, scrollViewer));

            var item = Assert.IsType<ComboBoxItem>(FindVisualChild<ComboBoxItem>(popup.Child));
            Assert.False(DragScrolling.IsScrollBarInteraction(item, scrollViewer));

            int selectedIndex = comboBox.SelectedIndex;
            PrecisionScrolling.SetDiscreteAnimationMilliseconds(scrollViewer, 0);
            scrollViewer.ScrollToVerticalOffset(scrollViewer.ScrollableHeight / 2);
            FlushPopupLayout(dispatcher, popup.Child, scrollViewer);
            double middleOffset = scrollViewer.VerticalOffset;

            RaiseWheel(scrollViewer, -120);
            FlushPopupLayout(dispatcher, popup.Child, scrollViewer);
            double downOffset = scrollViewer.VerticalOffset;
            Assert.True(downOffset > middleOffset,
                $"Expected wheel-down to increase the offset, but it changed from {middleOffset} to {downOffset}.");

            RaiseWheel(scrollViewer, 120);
            FlushPopupLayout(dispatcher, popup.Child, scrollViewer);
            Assert.True(scrollViewer.VerticalOffset < downOffset,
                $"Expected wheel-up to decrease the offset, but it changed from {downOffset} to {scrollViewer.VerticalOffset}.");
            Assert.Equal(selectedIndex, comboBox.SelectedIndex);
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

        private static T FindVisualChild<T>(DependencyObject parent,
            Func<T, bool> predicate = null) where T : DependencyObject
        {
            if (parent == null)
                return null;

            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T result && (predicate == null || predicate(result)))
                    return result;

                result = FindVisualChild(child, predicate);
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

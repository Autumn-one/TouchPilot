using GestureSign.Common.Input;
using GestureSign.Daemon.Triggers;
using ManagedWinapi.Windows;
using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using WindowsInput;
using Xunit;
using NativeMethods = GestureSign.Daemon.Native.NativeMethods;

namespace GestureSign.Tests
{
    public class WindowDragControllerIntegrationTests
    {
        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void DirectControllerMovesARealWindowAndSupportsReclutch()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    RunWindowDragTest();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The window drag integration test timed out.");
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static void RunWindowDragTest()
        {
            Point originalCursor = Cursor.Position;
            var controller = new WindowDragController();
            using (var form = new Form
            {
                Bounds = new Rectangle(160, 160, 360, 240),
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Text = "GestureSign window drag integration test"
            })
            {
                try
                {
                    form.Show();
                    Application.DoEvents();

                    var window = new SystemWindow(form.Handle);
                    RECT initialRectangle = window.Rectangle;
                    Point initialCursor = new Point(initialRectangle.Left + 80, initialRectangle.Top + 60);
                    Cursor.Position = initialCursor;

                    Assert.True(controller.Begin(window, 0.50, 0.50, TouchpadWindowDragImplementation.DirectSetWindowPos));
                    Thread.Sleep(25);
                    Assert.True(controller.Update(0.55, 0.52, 1));

                    for (int i = 0; i < 5; i++)
                    {
                        Application.DoEvents();
                        Thread.Sleep(20);
                    }

                    RECT movedRectangle = window.Rectangle;
                    Point movedCursor = Cursor.Position;
                    Screen screen = Screen.FromPoint(initialCursor);
                    int expectedX = (int)Math.Round(screen.Bounds.Width * 0.05);
                    int expectedY = (int)Math.Round(screen.Bounds.Height * 0.02);

                    Assert.InRange(movedCursor.X - initialCursor.X, expectedX - 3, expectedX + 3);
                    Assert.InRange(movedCursor.Y - initialCursor.Y, expectedY - 3, expectedY + 3);
                    Assert.InRange(movedRectangle.Left - initialRectangle.Left, expectedX - 4, expectedX + 4);
                    Assert.InRange(movedRectangle.Top - initialRectangle.Top, expectedY - 4, expectedY + 4);

                    controller.Pause();
                    Point pausedCursor = Cursor.Position;
                    Point reclutchedCursor = new Point(pausedCursor.X + 30, pausedCursor.Y + 20);
                    Cursor.Position = reclutchedCursor;
                    Assert.True(controller.Rebase(0.55, 0.52));
                    Assert.Equal(reclutchedCursor, Cursor.Position);
                    RECT reclutchedRectangle = window.Rectangle;
                    Thread.Sleep(25);
                    Assert.True(controller.Update(0.57, 0.53, 1));

                    for (int i = 0; i < 5; i++)
                    {
                        Application.DoEvents();
                        Thread.Sleep(20);
                    }

                    Point finalCursor = Cursor.Position;
                    RECT finalRectangle = window.Rectangle;
                    int reclutchExpectedX = (int)Math.Round(screen.Bounds.Width * 0.02);
                    int reclutchExpectedY = (int)Math.Round(screen.Bounds.Height * 0.01);

                    Assert.InRange(finalCursor.X - reclutchedCursor.X, reclutchExpectedX - 3, reclutchExpectedX + 3);
                    Assert.InRange(finalCursor.Y - reclutchedCursor.Y, reclutchExpectedY - 3, reclutchExpectedY + 3);
                    Assert.InRange(finalRectangle.Left - reclutchedRectangle.Left, reclutchExpectedX - 4, reclutchExpectedX + 4);
                    Assert.InRange(finalRectangle.Top - reclutchedRectangle.Top, reclutchExpectedY - 4, reclutchExpectedY + 4);
                }
                finally
                {
                    controller.End();
                    Cursor.Position = originalCursor;
                    form.Close();
                    Application.DoEvents();
                }
            }
        }

        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void SimulatedMouseControllerKeepsCursorPositionAndDragsCaption()
        {
            RunWithSimulatedMouseTestWindow((form, textBox, window, controller) =>
            {
                RECT initialRectangle = window.Rectangle;
                Point clientOrigin = (Point)form.Invoke(new Func<Point>(() => form.PointToScreen(Point.Empty)));
                var initialCursor = new Point(clientOrigin.X + 80,
                    initialRectangle.Top + (clientOrigin.Y - initialRectangle.Top) / 2);
                AssertCaptionPoint(window.HWnd, initialCursor);
                Cursor.Position = initialCursor;

                bool started = controller.Begin(window, 0.50, 0.50, TouchpadWindowDragImplementation.SimulatedMouseDrag);
                Assert.True(started, controller.LastFailure);
                Assert.Equal(initialCursor, Cursor.Position);
                Thread.Sleep(100);
                Assert.True(controller.Update(0.55, 0.52, 1));
                Thread.Sleep(120);

                Point firstMovedCursor = Cursor.Position;
                RECT firstMovedRectangle = window.Rectangle;
                Screen screen = Screen.FromPoint(initialCursor);
                int firstExpectedX = (int)Math.Round(screen.Bounds.Width * 0.05);
                int firstExpectedY = (int)Math.Round(screen.Bounds.Height * 0.02);

                Assert.InRange(firstMovedCursor.X - initialCursor.X, firstExpectedX - 4, firstExpectedX + 4);
                Assert.InRange(firstMovedCursor.Y - initialCursor.Y, firstExpectedY - 4, firstExpectedY + 4);
                Assert.InRange(firstMovedRectangle.Left - initialRectangle.Left, firstExpectedX - 6, firstExpectedX + 6);
                Assert.InRange(firstMovedRectangle.Top - initialRectangle.Top, firstExpectedY - 6, firstExpectedY + 6);

                controller.Pause();
                Thread.Sleep(80);
                var reclutchedCursor = new Point(firstMovedCursor.X + 20, firstMovedCursor.Y);
                AssertCaptionPoint(window.HWnd, reclutchedCursor);
                Cursor.Position = reclutchedCursor;
                Assert.True(controller.Rebase(0.55, 0.52));
                Assert.Equal(reclutchedCursor, Cursor.Position);
                RECT reclutchedRectangle = window.Rectangle;
                Thread.Sleep(80);
                Assert.True(controller.Update(0.57, 0.53, 1));
                Thread.Sleep(180);
                controller.End();
                Thread.Sleep(120);

                RECT movedRectangle = window.Rectangle;
                Point movedCursor = Cursor.Position;
                int secondExpectedX = (int)Math.Round(screen.Bounds.Width * 0.02);
                int secondExpectedY = (int)Math.Round(screen.Bounds.Height * 0.01);

                Assert.InRange(reclutchedRectangle.Left - firstMovedRectangle.Left, -1, 1);
                Assert.InRange(reclutchedRectangle.Top - firstMovedRectangle.Top, -1, 1);
                Assert.InRange(movedCursor.X - reclutchedCursor.X, secondExpectedX - 4, secondExpectedX + 4);
                Assert.InRange(movedCursor.Y - reclutchedCursor.Y, secondExpectedY - 4, secondExpectedY + 4);
                Assert.InRange(movedRectangle.Left - reclutchedRectangle.Left, secondExpectedX - 6, secondExpectedX + 6);
                Assert.InRange(movedRectangle.Top - reclutchedRectangle.Top, secondExpectedY - 6, secondExpectedY + 6);
            });
        }

        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void SimulatedMouseControllerSelectsClientTextWithoutMovingWindow()
        {
            RunWithSimulatedMouseTestWindow((form, textBox, window, controller) =>
            {
                RECT initialRectangle = window.Rectangle;
                Point initialCursor = (Point)textBox.Invoke(new Func<Point>(() => textBox.PointToScreen(new Point(8, textBox.ClientSize.Height / 2))));
                IntPtr hitWindow = NativeMethods.WindowFromPoint(new NativeMethods.Point(initialCursor.X, initialCursor.Y));
                Assert.Equal(form.Handle, NativeMethods.GetAncestor(hitWindow, NativeMethods.GA_ROOT));
                Cursor.Position = initialCursor;

                bool started = controller.Begin(window, 0.50, 0.50, TouchpadWindowDragImplementation.SimulatedMouseDrag);
                Assert.True(started, controller.LastFailure);
                Assert.Equal(initialCursor, Cursor.Position);
                Thread.Sleep(100);
                Assert.True(controller.Update(0.58, 0.50, 1));
                Thread.Sleep(160);
                controller.End();
                Thread.Sleep(100);

                RECT finalRectangle = window.Rectangle;
                int selectionLength = (int)textBox.Invoke(new Func<int>(() => textBox.SelectionLength));
                Assert.True(selectionLength > 0, "Dragging in the client text box did not select text.");
                Assert.InRange(finalRectangle.Left - initialRectangle.Left, -1, 1);
                Assert.InRange(finalRectangle.Top - initialRectangle.Top, -1, 1);
            });
        }

        private static void RunWithSimulatedMouseTestWindow(
            Action<Form, TextBox, SystemWindow, WindowDragController> test)
        {
            Point originalCursor = Cursor.Position;
            var controller = new WindowDragController();
            using (var ready = new ManualResetEvent(false))
            using (var closed = new ManualResetEvent(false))
            {
                Form form = null;
                TextBox textBox = null;
                Exception windowThreadFailure = null;
                var windowThread = new Thread(() =>
                {
                    try
                    {
                        form = new Form
                        {
                            Bounds = new Rectangle(160, 160, 420, 260),
                            FormBorderStyle = FormBorderStyle.Sizable,
                            ShowInTaskbar = false,
                            StartPosition = FormStartPosition.Manual,
                            TopMost = true,
                            Text = "GestureSign simulated mouse drag integration test"
                        };
                        textBox = new TextBox
                        {
                            Bounds = new Rectangle(24, 50, 340, 32),
                            Font = new Font("Segoe UI", 12),
                            Text = "GestureSign simulated mouse drag can select this text"
                        };
                        form.Controls.Add(textBox);
                        form.Shown += (sender, args) =>
                        {
                            form.Activate();
                            form.BringToFront();
                            ready.Set();
                        };
                        form.FormClosed += (sender, args) => closed.Set();
                        Application.Run(form);
                    }
                    catch (Exception exception)
                    {
                        windowThreadFailure = exception;
                        ready.Set();
                        closed.Set();
                    }
                });
                windowThread.SetApartmentState(ApartmentState.STA);
                windowThread.Start();

                try
                {
                    Assert.True(ready.WaitOne(TimeSpan.FromSeconds(5)), "The test window did not open.");
                    if (windowThreadFailure != null)
                        ExceptionDispatchInfo.Capture(windowThreadFailure).Throw();
                    Thread.Sleep(150);

                    var window = new SystemWindow(form.Handle);
                    Assert.True(NativeMethods.SetWindowPos(
                        window.HWnd,
                        new IntPtr(-1),
                        0,
                        0,
                        0,
                        0,
                        NativeMethods.SWP.SWP_NOMOVE |
                        NativeMethods.SWP.SWP_NOSIZE |
                        NativeMethods.SWP.SWP_SHOWWINDOW));
                    Thread.Sleep(50);
                    Point activationPoint = (Point)form.Invoke(new Func<Point>(() =>
                        form.PointToScreen(new Point(form.ClientSize.Width - 20, form.ClientSize.Height - 20))));
                    IntPtr hitWindow = NativeMethods.WindowFromPoint(new NativeMethods.Point(activationPoint.X, activationPoint.Y));
                    Assert.Equal(window.HWnd, NativeMethods.GetAncestor(hitWindow, NativeMethods.GA_ROOT));
                    Cursor.Position = activationPoint;
                    new InputSimulator().Mouse.LeftButtonClick();
                    Thread.Sleep(100);
                    Assert.Equal(window.HWnd, SystemWindow.ForegroundWindow.HWnd);
                    test(form, textBox, window, controller);
                }
                finally
                {
                    controller.End();
                    Cursor.Position = originalCursor;
                    if (form?.IsHandleCreated == true)
                        form.BeginInvoke(new Action(form.Close));
                    closed.WaitOne(TimeSpan.FromSeconds(3));
                    windowThread.Join(TimeSpan.FromSeconds(3));
                }
            }
        }

        private static void AssertCaptionPoint(IntPtr windowHandle, Point point)
        {
            IntPtr hitTestResult;
            int packedPoint = ((point.Y & 0xffff) << 16) | (point.X & 0xffff);
            IntPtr callResult = NativeMethods.SendMessageTimeout(
                windowHandle,
                NativeMethods.WM_NCHITTEST,
                IntPtr.Zero,
                new IntPtr(packedPoint),
                NativeMethods.SMTO_BLOCK | NativeMethods.SMTO_ABORTIFHUNG,
                100,
                out hitTestResult);
            Assert.NotEqual(IntPtr.Zero, callResult);
            Assert.Equal(NativeMethods.HTCAPTION, hitTestResult.ToInt64());
        }
    }
}

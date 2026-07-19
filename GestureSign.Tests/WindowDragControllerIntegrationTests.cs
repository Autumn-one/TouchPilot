using GestureSign.Common.Input;
using GestureSign.Daemon.Triggers;
using ManagedWinapi.Windows;
using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

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
                    Assert.True(controller.Rebase(0.55, 0.52));
                    Point reclutchedCursor = Cursor.Position;
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
        public void SimulatedCaptionControllerMovesARealWindowAndSupportsReclutch()
        {
            Point originalCursor = Cursor.Position;
            var controller = new WindowDragController();
            var ready = new ManualResetEvent(false);
            var closed = new ManualResetEvent(false);
            Form form = null;
            Exception windowThreadFailure = null;
            int clientMouseDownCount = 0;
            var windowThread = new Thread(() =>
            {
                try
                {
                    form = new Form
                    {
                        Bounds = new Rectangle(160, 160, 360, 240),
                        FormBorderStyle = FormBorderStyle.Sizable,
                        ShowInTaskbar = false,
                        StartPosition = FormStartPosition.Manual,
                        TopMost = true,
                        Text = "GestureSign simulated caption drag integration test"
                    };
                    form.MouseDown += (sender, args) => Interlocked.Increment(ref clientMouseDownCount);
                    form.Shown += (sender, args) =>
                    {
                        form.TopMost = true;
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
                Assert.True((window.ExtendedStyle & WindowExStyleFlags.TOPMOST) != 0,
                    "The simulated-caption test window was not placed in the topmost Z-order band.");
                RECT initialRectangle = window.Rectangle;
                Cursor.Position = new Point(initialRectangle.Left + 80, initialRectangle.Top + 80);

                bool started = controller.Begin(window, 0.50, 0.50, TouchpadWindowDragImplementation.SimulatedCaptionDrag);
                Assert.True(started, controller.LastFailure);
                Point captionCursor = Cursor.Position;
                Thread.Sleep(100);
                Assert.True(controller.Update(0.55, 0.52, 1));
                Thread.Sleep(120);

                Point firstMovedCursor = Cursor.Position;
                RECT firstMovedRectangle = window.Rectangle;
                Screen screen = Screen.FromPoint(captionCursor);
                int firstExpectedX = (int)Math.Round(screen.Bounds.Width * 0.05);
                int firstExpectedY = (int)Math.Round(screen.Bounds.Height * 0.02);

                Assert.Equal(0, Volatile.Read(ref clientMouseDownCount));
                Assert.InRange(firstMovedCursor.X - captionCursor.X, firstExpectedX - 4, firstExpectedX + 4);
                Assert.InRange(firstMovedCursor.Y - captionCursor.Y, firstExpectedY - 4, firstExpectedY + 4);
                Assert.InRange(firstMovedRectangle.Left - initialRectangle.Left, firstExpectedX - 6, firstExpectedX + 6);
                Assert.InRange(firstMovedRectangle.Top - initialRectangle.Top, firstExpectedY - 6, firstExpectedY + 6);

                controller.Pause();
                Thread.Sleep(80);
                Assert.True(controller.Rebase(0.55, 0.52));
                Point reclutchedCursor = Cursor.Position;
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

                Assert.Equal(0, Volatile.Read(ref clientMouseDownCount));
                Assert.InRange(reclutchedRectangle.Left - firstMovedRectangle.Left, -1, 1);
                Assert.InRange(reclutchedRectangle.Top - firstMovedRectangle.Top, -1, 1);
                Assert.InRange(movedCursor.X - reclutchedCursor.X, secondExpectedX - 4, secondExpectedX + 4);
                Assert.InRange(movedCursor.Y - reclutchedCursor.Y, secondExpectedY - 4, secondExpectedY + 4);
                Assert.InRange(movedRectangle.Left - reclutchedRectangle.Left, secondExpectedX - 6, secondExpectedX + 6);
                Assert.InRange(movedRectangle.Top - reclutchedRectangle.Top, secondExpectedY - 6, secondExpectedY + 6);
            }
            finally
            {
                controller.End();
                Cursor.Position = originalCursor;
                if (form?.IsHandleCreated == true)
                    form.BeginInvoke(new Action(form.Close));
                closed.WaitOne(TimeSpan.FromSeconds(3));
                windowThread.Join(TimeSpan.FromSeconds(3));
                ready.Dispose();
                closed.Dispose();
            }
        }
    }
}

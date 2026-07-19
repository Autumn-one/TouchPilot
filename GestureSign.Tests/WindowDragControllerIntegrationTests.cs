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
        public void ControllerMovesARealWindowAndCursor()
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

                    Assert.True(controller.Begin(window, 0.50, 0.50));
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
    }
}

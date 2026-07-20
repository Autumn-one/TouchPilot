using GestureSign.Daemon.Triggers;
using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using WindowsInput;
using Xunit;

namespace GestureSign.Tests
{
    public class TouchpadWheelSuppressorTests
    {
        [Theory]
        [InlineData(0x020A)]
        [InlineData(0x020E)]
        public void ActiveSuppressionRecognizesVerticalAndHorizontalWheelMessages(int message)
        {
            Assert.True(TouchpadWheelSuppressor.ShouldSuppressWheel(true, message));
            Assert.False(TouchpadWheelSuppressor.ShouldSuppressWheel(false, message));
        }

        [Theory]
        [InlineData(0x0200)]
        [InlineData(0x0201)]
        [InlineData(0x0202)]
        public void ActiveSuppressionLeavesNonWheelMouseMessagesUntouched(int message)
        {
            Assert.False(TouchpadWheelSuppressor.ShouldSuppressWheel(true, message));
        }

        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void ActiveSuppressorBlocksWheelMessagesFromARealWindow()
        {
            Point originalCursor = Cursor.Position;
            using (var ready = new ManualResetEvent(false))
            using (var closed = new ManualResetEvent(false))
            {
                WheelProbeForm form = null;
                TouchpadWheelSuppressor suppressor = null;
                Exception windowThreadFailure = null;
                var windowThread = new Thread(() =>
                {
                    try
                    {
                        suppressor = new TouchpadWheelSuppressor();
                        form = new WheelProbeForm
                        {
                            Bounds = new Rectangle(180, 180, 360, 240),
                            ShowInTaskbar = false,
                            StartPosition = FormStartPosition.Manual,
                            TopMost = true,
                            Text = "GestureSign wheel suppression integration test"
                        };
                        if (!suppressor.StartMonitoring())
                            throw new InvalidOperationException(suppressor.LastFailure);
                        form.Shown += (sender, args) => ready.Set();
                        form.FormClosed += (sender, args) =>
                        {
                            suppressor.Dispose();
                            closed.Set();
                        };
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
                    Assert.True(ready.WaitOne(TimeSpan.FromSeconds(5)), "The wheel probe window did not open.");
                    if (windowThreadFailure != null)
                        ExceptionDispatchInfo.Capture(windowThreadFailure).Throw();
                    form.Invoke(new Action(() =>
                    {
                        form.Activate();
                        form.BringToFront();
                        Cursor.Position = form.PointToScreen(new Point(form.ClientSize.Width / 2,
                            form.ClientSize.Height / 2));
                    }));
                    Thread.Sleep(100);

                    SendBothWheelDirections();
                    Thread.Sleep(100);
                    Assert.True((int)form.Invoke(new Func<int>(() => form.VerticalWheelMessages)) > 0,
                        "The baseline vertical wheel message did not reach the real window.");
                    Assert.True((int)form.Invoke(new Func<int>(() => form.HorizontalWheelMessages)) > 0,
                        "The baseline horizontal wheel message did not reach the real window.");

                    form.Invoke(new Action(form.ResetWheelMessages));
                    suppressor.SuppressWheel = true;
                    SendBothWheelDirections();
                    Thread.Sleep(100);
                    Assert.Equal(0, (int)form.Invoke(new Func<int>(() => form.VerticalWheelMessages)));
                    Assert.Equal(0, (int)form.Invoke(new Func<int>(() => form.HorizontalWheelMessages)));

                    form.Invoke(new Action(suppressor.StopMonitoring));
                    SendBothWheelDirections();
                    Thread.Sleep(100);
                    Assert.True((int)form.Invoke(new Func<int>(() => form.VerticalWheelMessages)) > 0);
                    Assert.True((int)form.Invoke(new Func<int>(() => form.HorizontalWheelMessages)) > 0);
                }
                finally
                {
                    Cursor.Position = originalCursor;
                    if (form?.IsHandleCreated == true)
                        form.BeginInvoke(new Action(form.Close));
                    closed.WaitOne(TimeSpan.FromSeconds(3));
                    windowThread.Join(TimeSpan.FromSeconds(3));
                }
            }
        }

        private static void SendBothWheelDirections()
        {
            var simulator = new InputSimulator();
            simulator.Mouse.VerticalScroll(1);
            simulator.Mouse.HorizontalScroll(1);
        }

        private sealed class WheelProbeForm : Form
        {
            private const int WmMouseWheel = 0x020A;
            private const int WmMouseHorizontalWheel = 0x020E;

            public int VerticalWheelMessages { get; private set; }
            public int HorizontalWheelMessages { get; private set; }

            public void ResetWheelMessages()
            {
                VerticalWheelMessages = 0;
                HorizontalWheelMessages = 0;
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == WmMouseWheel)
                    VerticalWheelMessages++;
                else if (message.Msg == WmMouseHorizontalWheel)
                    HorizontalWheelMessages++;
                base.WndProc(ref message);
            }
        }
    }
}

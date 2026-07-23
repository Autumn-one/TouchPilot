using GestureSign.Common.Input;
using GestureSign.Daemon.Triggers;
using ManagedWinapi.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsInput;
using Xunit;
using NativeMethods = GestureSign.Daemon.Native.NativeMethods;

namespace GestureSign.Tests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class DesktopInputIntegrationCollection
    {
        public const string Name = "Desktop input integration";
    }

    [Collection(DesktopInputIntegrationCollection.Name)]
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
        public void DirectControllerRetargetsWindowUnderCursorWhenReclutched()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    RunWindowDragRetargetTest();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The window drag retarget integration test timed out.");
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static void RunWindowDragRetargetTest()
        {
            Point originalCursor = Cursor.Position;
            var controller = new WindowDragController();
            Rectangle workingArea = Screen.FromPoint(originalCursor).WorkingArea;
            int width = Math.Min(320, workingArea.Width / 3);
            int height = Math.Min(220, workingArea.Height / 2);
            using (var firstForm = CreateDirectDragTestForm(
                new Rectangle(workingArea.Left + 40, workingArea.Top + 80, width, height), "first"))
            using (var secondForm = CreateDirectDragTestForm(
                new Rectangle(workingArea.Right - width - 40, workingArea.Top + 80, width, height), "second"))
            {
                try
                {
                    firstForm.Show();
                    secondForm.Show();
                    Application.DoEvents();

                    var firstWindow = new SystemWindow(firstForm.Handle);
                    var secondWindow = new SystemWindow(secondForm.Handle);
                    ActivateForm(secondForm);
                    Assert.Equal(secondWindow.HWnd, SystemWindow.ForegroundWindow.HWnd);
                    RECT firstInitialRectangle = firstWindow.Rectangle;
                    Point firstCursor = new Point(firstInitialRectangle.Left + 80, firstInitialRectangle.Top + 60);
                    Cursor.Position = firstCursor;

                    Assert.True(controller.Begin(firstWindow, 0.50, 0.50,
                        TouchpadWindowDragImplementation.DirectSetWindowPos, true));
                    Thread.Sleep(25);
                    Assert.True(controller.Update(0.52, 0.51, 1));
                    PumpWindowMessages();
                    Assert.Equal(firstWindow.HWnd, SystemWindow.ForegroundWindow.HWnd);

                    controller.Pause();
                    RECT firstPausedRectangle = firstWindow.Rectangle;
                    RECT secondInitialRectangle = secondWindow.Rectangle;
                    Point secondCursor = new Point(secondInitialRectangle.Left + 80, secondInitialRectangle.Top + 60);
                    Cursor.Position = secondCursor;
                    SystemWindow windowUnderCursor = SystemWindow.FromPointEx(secondCursor.X, secondCursor.Y, true, true);

                    Assert.NotNull(windowUnderCursor);
                    Assert.Equal(secondWindow.HWnd, windowUnderCursor.HWnd);
                    Assert.True(controller.Rebase(windowUnderCursor, 0.52, 0.51));
                    Assert.Equal(secondCursor, Cursor.Position);
                    Thread.Sleep(25);
                    Assert.True(controller.Update(0.55, 0.53, 1));
                    PumpWindowMessages();
                    Assert.Equal(secondWindow.HWnd, SystemWindow.ForegroundWindow.HWnd);

                    RECT firstFinalRectangle = firstWindow.Rectangle;
                    RECT secondFinalRectangle = secondWindow.Rectangle;
                    Point finalCursor = Cursor.Position;
                    Screen screen = Screen.FromPoint(secondCursor);
                    int expectedX = (int)Math.Round(screen.Bounds.Width * 0.03);
                    int expectedY = (int)Math.Round(screen.Bounds.Height * 0.02);

                    Assert.InRange(firstFinalRectangle.Left - firstPausedRectangle.Left, -1, 1);
                    Assert.InRange(firstFinalRectangle.Top - firstPausedRectangle.Top, -1, 1);
                    Assert.InRange(finalCursor.X - secondCursor.X, expectedX - 3, expectedX + 3);
                    Assert.InRange(finalCursor.Y - secondCursor.Y, expectedY - 3, expectedY + 3);
                    Assert.InRange(secondFinalRectangle.Left - secondInitialRectangle.Left, expectedX - 4, expectedX + 4);
                    Assert.InRange(secondFinalRectangle.Top - secondInitialRectangle.Top, expectedY - 4, expectedY + 4);
                }
                finally
                {
                    controller.End();
                    Cursor.Position = originalCursor;
                    secondForm.Close();
                    firstForm.Close();
                    Application.DoEvents();
                }
            }
        }

        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void DirectControllerKeepsForegroundWindowWhenBringToFrontIsDisabled()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    RunWindowDragWithoutActivationTest();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.True(thread.Join(TimeSpan.FromSeconds(10)),
                "The disabled bring-to-front integration test timed out.");
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static void RunWindowDragWithoutActivationTest()
        {
            Point originalCursor = Cursor.Position;
            var controller = new WindowDragController();
            Rectangle workingArea = Screen.FromPoint(originalCursor).WorkingArea;
            int width = Math.Min(320, workingArea.Width / 3);
            int height = Math.Min(220, workingArea.Height / 2);
            using (var targetForm = CreateDirectDragTestForm(
                new Rectangle(workingArea.Left + 40, workingArea.Top + 80, width, height), "inactive target"))
            using (var foregroundForm = CreateDirectDragTestForm(
                new Rectangle(workingArea.Right - width - 40, workingArea.Top + 80, width, height), "foreground"))
            {
                try
                {
                    targetForm.Show();
                    foregroundForm.Show();
                    Application.DoEvents();

                    var targetWindow = new SystemWindow(targetForm.Handle);
                    var foregroundWindow = new SystemWindow(foregroundForm.Handle);
                    ActivateForm(foregroundForm);
                    Assert.Equal(foregroundWindow.HWnd, SystemWindow.ForegroundWindow.HWnd);

                    RECT targetRectangle = targetWindow.Rectangle;
                    Cursor.Position = new Point(targetRectangle.Left + 80, targetRectangle.Top + 60);
                    Assert.True(controller.Begin(targetWindow, 0.50, 0.50,
                        TouchpadWindowDragImplementation.DirectSetWindowPos, false));
                    PumpWindowMessages();

                    Assert.Equal(foregroundWindow.HWnd, SystemWindow.ForegroundWindow.HWnd);
                }
                finally
                {
                    controller.End();
                    Cursor.Position = originalCursor;
                    foregroundForm.Close();
                    targetForm.Close();
                    Application.DoEvents();
                }
            }
        }

        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void DirectControllerActivatesWindowAcrossTheForegroundLock()
        {
            RunInStaThread(reportStage => RunExternalForegroundWindowDragTest(true, false, reportStage),
                "The cross-process foreground activation test timed out.");
        }

        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void DirectControllerKeepsExternalForegroundWindowWhenBringToFrontIsDisabled()
        {
            RunInStaThread(reportStage => RunExternalForegroundWindowDragTest(false, false, reportStage),
                "The cross-process disabled foreground activation test timed out.");
        }

        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void DirectControllerPreservesHeldModifierAcrossForegroundActivation()
        {
            RunInStaThread(reportStage => RunExternalForegroundWindowDragTest(true, true, reportStage),
                "The held-modifier foreground activation test timed out.");
        }

        private static void RunExternalForegroundWindowDragTest(bool bringToForeground,
            bool holdControl,
            Action<string> reportStage)
        {
            reportStage("creating the target window");
            Point originalCursor = Cursor.Position;
            bool controlInitiallyDown = holdControl && IsControlKeyDown();
            var controller = new WindowDragController();
            Rectangle workingArea = Screen.FromPoint(originalCursor).WorkingArea;
            int width = Math.Min(360, Math.Max(240, workingArea.Width / 4));
            int height = Math.Min(240, Math.Max(180, workingArea.Height / 3));
            var targetBounds = new Rectangle(workingArea.Left + 40, workingArea.Top + 80, width, height);
            var foregroundBounds = new Rectangle(workingArea.Right - width - 40,
                workingArea.Top + 80, width, height);

            using (var targetForm = new Form
            {
                Bounds = targetBounds,
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                TopMost = false,
                Text = "GestureSign cross-process drag target"
            })
            {
                ExternalForegroundWindow foregroundWindow = null;
                try
                {
                    if (holdControl)
                        Assert.False(controlInitiallyDown, "Ctrl was already pressed before the integration test.");

                    targetForm.Show();
                    Application.DoEvents();

                    var targetWindow = new SystemWindow(targetForm.Handle);
                    reportStage("starting the external foreground window");
                    foregroundWindow = ExternalForegroundWindow.Start(foregroundBounds, holdControl, reportStage);
                    reportStage("waiting for the external window to become foreground");
                    Assert.True(WaitForForegroundWindow(foregroundWindow.Handle),
                        foregroundWindow.Diagnostics ?? "The external window did not become foreground.");
                    if (holdControl)
                        Assert.True(IsControlKeyDown(), "The external process did not hold Ctrl down.");

                    RECT targetRectangle = targetWindow.Rectangle;
                    Point targetCursor = new Point(targetRectangle.Left + 80, targetRectangle.Top + 60);
                    Cursor.Position = targetCursor;
                    Assert.Equal(foregroundWindow.Handle, SystemWindow.ForegroundWindow.HWnd);
                    Assert.False(targetWindow.TopMost);
                    reportStage("establishing the foreground lock");
                    bool foregroundLockObserved = !NativeMethods.SetForegroundWindow(targetWindow.HWnd);
                    if (!foregroundLockObserved)
                    {
                        Assert.True(NativeMethods.SetForegroundWindow(foregroundWindow.Handle));
                        Assert.True(WaitForForegroundWindow(foregroundWindow.Handle),
                            "The external foreground test window could not be reactivated.");
                    }

                    reportStage("starting the direct drag controller");
                    Assert.True(controller.Begin(targetWindow, 0.50, 0.50,
                        TouchpadWindowDragImplementation.DirectSetWindowPos, bringToForeground),
                        controller.LastFailure);
                    PumpWindowMessages();

                    if (bringToForeground)
                    {
                        reportStage("waiting for the target window to become foreground");
                        Assert.True(WaitForForegroundWindow(targetWindow.HWnd), controller.LastFailure);
                        Assert.Null(controller.LastFailure);
                    }
                    else
                    {
                        Assert.Equal(foregroundWindow.Handle, SystemWindow.ForegroundWindow.HWnd);
                    }

                    Assert.Equal(targetCursor, Cursor.Position);
                    Assert.False(targetWindow.TopMost);
                    if (holdControl)
                        Assert.True(IsControlKeyDown(), "Foreground activation released the held Ctrl key.");
                }
                finally
                {
                    reportStage("ending the direct drag controller");
                    controller.End();
                    Cursor.Position = originalCursor;
                    reportStage("disposing the external foreground window");
                    foregroundWindow?.Dispose();
                    if (holdControl && !controlInitiallyDown)
                        new InputSimulator().Keyboard.KeyUp(WindowsInput.Native.VirtualKeyCode.CONTROL);
                    reportStage("closing the target window");
                    targetForm.Close();
                    Application.DoEvents();
                }
            }

            reportStage("completed");
        }

        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void InteriorThreeFingerReleaseDoesNotDriveARealWindow()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    RunInteriorThreeFingerReleaseWindowTest();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.True(thread.Join(TimeSpan.FromSeconds(10)),
                "The three-finger release window integration test timed out.");
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static void RunInteriorThreeFingerReleaseWindowTest()
        {
            Point originalCursor = Cursor.Position;
            var controller = new WindowDragController();
            var recognizer = new TouchpadInteractionRecognizer(new TouchpadInteractionOptions
            {
                WindowDragMode = TouchpadWindowDragMode.BottomEdgeAnchor
            });
            Rectangle workingArea = Screen.FromPoint(originalCursor).WorkingArea;
            using (var form = CreateDirectDragTestForm(
                new Rectangle(workingArea.Left + 80, workingArea.Top + 80, 360, 240),
                "three-finger release"))
            {
                try
                {
                    form.Show();
                    Application.DoEvents();

                    var window = new SystemWindow(form.Handle);
                    RECT initialRectangle = window.Rectangle;
                    Point initialCursor = new Point(initialRectangle.Left + 80, initialRectangle.Top + 60);
                    Cursor.Position = initialCursor;
                    Point effectiveInitialCursor = Cursor.Position;

                    TouchpadInteractionFrameResult pressed = ProcessWindowDragFrame(recognizer,
                        Frame(Contact(1, 0.3, 0.4), Contact(2, 0.5, 0.4), Contact(3, 0.7, 0.4)),
                        0, controller, window);
                    TouchpadInteractionFrameResult released = ProcessWindowDragFrame(recognizer,
                        Frame(Contact(1, 0.3, 0.4), Contact(2, 0.5, 0.4), ReleasedContact(3, 0.7, 0.4)),
                        1, controller, window);
                    TouchpadInteractionFrameResult firstMove = ProcessWindowDragFrame(recognizer,
                        Frame(Contact(1, 0.3, 0.4), Contact(2, 0.53, 0.4)),
                        20, controller, window);
                    Thread.Sleep(25);
                    TouchpadInteractionFrameResult continuedMove = ProcessWindowDragFrame(recognizer,
                        Frame(Contact(1, 0.32, 0.4), Contact(2, 0.56, 0.4)),
                        50, controller, window);
                    PumpWindowMessages();

                    Assert.False(pressed.ClaimInput);
                    Assert.Empty(pressed.Events);
                    Assert.False(released.ClaimInput);
                    Assert.Empty(released.Events);
                    Assert.False(firstMove.ClaimInput);
                    Assert.Empty(firstMove.Events);
                    Assert.False(continuedMove.ClaimInput);
                    Assert.Empty(continuedMove.Events);
                    Assert.Equal(effectiveInitialCursor, Cursor.Position);

                    RECT finalRectangle = window.Rectangle;
                    Assert.InRange(finalRectangle.Left - initialRectangle.Left, -1, 1);
                    Assert.InRange(finalRectangle.Top - initialRectangle.Top, -1, 1);
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

        private static TouchpadInteractionFrameResult ProcessWindowDragFrame(
            TouchpadInteractionRecognizer recognizer,
            IReadOnlyList<TouchpadContact> contacts,
            long timestampMilliseconds,
            WindowDragController controller,
            SystemWindow window)
        {
            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(contacts, timestampMilliseconds);
            foreach (TouchpadInteractionEvent interactionEvent in result.Events)
            {
                switch (interactionEvent.EventType)
                {
                    case TouchpadInteractionEventType.WindowDragStarted:
                        Assert.True(controller.Begin(window, interactionEvent.NormalizedX,
                            interactionEvent.NormalizedY, TouchpadWindowDragImplementation.DirectSetWindowPos));
                        break;
                    case TouchpadInteractionEventType.WindowDragMoved:
                        Assert.True(controller.Update(interactionEvent.NormalizedX,
                            interactionEvent.NormalizedY, 1));
                        break;
                    case TouchpadInteractionEventType.WindowDragEnded:
                        controller.End();
                        break;
                }
            }
            return result;
        }

        private static TouchpadContact Contact(int id, double x, double y)
        {
            return new TouchpadContact(id, DeviceStates.Tip, x, y);
        }

        private static TouchpadContact ReleasedContact(int id, double x, double y)
        {
            return new TouchpadContact(id, DeviceStates.None, x, y);
        }

        private static IReadOnlyList<TouchpadContact> Frame(params TouchpadContact[] contacts)
        {
            return contacts;
        }

        private static Form CreateDirectDragTestForm(Rectangle bounds, string label)
        {
            return new Form
            {
                Bounds = bounds,
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                TopMost = true,
                Text = $"GestureSign direct drag {label} window"
            };
        }

        private static void ActivateForm(Form form)
        {
            Point activationPoint = form.PointToScreen(new Point(
                Math.Max(1, form.ClientSize.Width - 20),
                Math.Max(1, form.ClientSize.Height - 20)));
            Cursor.Position = activationPoint;
            new InputSimulator().Mouse.LeftButtonClick();
            PumpWindowMessages();
        }

        private static void PumpWindowMessages()
        {
            for (int i = 0; i < 5; i++)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
        }

        private static void RunInStaThread(Action<Action<string>> test, string timeoutMessage)
        {
            Exception failure = null;
            string currentStage = "starting the STA thread";
            var thread = new Thread(() =>
            {
                try
                {
                    test(stage => Volatile.Write(ref currentStage, stage));
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.True(thread.Join(TimeSpan.FromSeconds(20)),
                $"{timeoutMessage} Last stage: {Volatile.Read(ref currentStage)}.");
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static bool WaitForForegroundWindow(IntPtr windowHandle)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(3))
            {
                if (SystemWindow.ForegroundWindow.HWnd == windowHandle)
                    return true;

                Application.DoEvents();
                Thread.Sleep(20);
            }

            return SystemWindow.ForegroundWindow.HWnd == windowHandle;
        }

        private static bool IsControlKeyDown()
        {
            return (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
        }

        private sealed class ExternalForegroundWindow : IDisposable
        {
            private const string HostFileName = "GestureSign.Tests.DesktopHost.exe";
            private readonly Process _process;

            private ExternalForegroundWindow(Process process, IntPtr handle, string diagnostics)
            {
                _process = process;
                Handle = handle;
                Diagnostics = diagnostics;
            }

            public IntPtr Handle { get; }
            public string Diagnostics { get; }

            public static ExternalForegroundWindow Start(Rectangle bounds,
                bool holdControl,
                Action<string> reportStage)
            {
                reportStage("locating the external foreground test host");
                string hostPath = Path.Combine(AppContext.BaseDirectory, HostFileName);
                if (!File.Exists(hostPath))
                    throw new FileNotFoundException("The external foreground test host was not built.", hostPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = hostPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add(bounds.X.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add(bounds.Y.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add(bounds.Width.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add(bounds.Height.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add(holdControl.ToString(CultureInfo.InvariantCulture));

                reportStage("launching the external foreground process");
                Process process = Process.Start(startInfo);
                if (process == null)
                    throw new InvalidOperationException("The external foreground test process could not be started.");

                reportStage("waiting for the external foreground process output");
                Task<string> readHandle = process.StandardOutput.ReadLineAsync();
                Task<string> readError = process.StandardError.ReadToEndAsync();
                if (!readHandle.Wait(TimeSpan.FromSeconds(30)))
                {
                    reportStage("stopping the timed-out foreground process");
                    StopProcess(process);
                    throw new InvalidOperationException(
                        "The external foreground test window did not start. " + ReadError(readError));
                }

                reportStage("validating the external foreground process output");
                string handleText = readHandle.GetAwaiter().GetResult();
                string[] activationState = handleText?.Split('|') ?? Array.Empty<string>();
                if (activationState.Length == 0 || !long.TryParse(activationState[0], out long handleValue) ||
                    handleValue == 0)
                {
                    reportStage("stopping the invalid foreground process");
                    StopProcess(process);
                    throw new InvalidOperationException(
                        $"The external foreground test returned an invalid handle '{handleText}'. " +
                        ReadError(readError));
                }

                return new ExternalForegroundWindow(process, new IntPtr(handleValue),
                    $"External activation state: {handleText}");
            }

            public void Dispose()
            {
                if (_process == null)
                    return;

                if (!_process.HasExited && Handle != IntPtr.Zero)
                    NativeMethods.PostMessage(new System.Runtime.InteropServices.HandleRef(this, Handle),
                        NativeMethods.WmClose, 0, 0);

                StopProcess(_process);
            }

            private static void StopProcess(Process process)
            {
                if (process == null)
                    return;

                try
                {
                    if (!process.HasExited && !process.WaitForExit(3000))
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }

            private static string ReadError(Task<string> readError)
            {
                try
                {
                    return readError.Wait(TimeSpan.FromMilliseconds(500))
                        ? readError.GetAwaiter().GetResult()
                        : "No stderr output was available within 500 ms.";
                }
                catch (Exception exception)
                {
                    return $"stderr could not be read ({exception.GetType().Name}).";
                }
            }
        }

        [Theory]
        [InlineData(TouchpadWindowDragImplementation.SimulatedMouseDrag)]
        [InlineData(TouchpadWindowDragImplementation.ThreeFingerDrag)]
        [Trait("Category", "WindowsIntegration")]
        public void SimulatedMouseControllerKeepsCursorPositionAndDragsCaption(
            TouchpadWindowDragImplementation implementation)
        {
            RunWithSimulatedMouseTestWindow((form, textBox, window, controller) =>
            {
                RECT initialRectangle = window.Rectangle;
                Point clientOrigin = (Point)form.Invoke(new Func<Point>(() => form.PointToScreen(Point.Empty)));
                var initialCursor = new Point(clientOrigin.X + 80,
                    initialRectangle.Top + (clientOrigin.Y - initialRectangle.Top) / 2);
                AssertCaptionPoint(window.HWnd, initialCursor);
                Cursor.Position = initialCursor;

                bool started = controller.Begin(window, 0.50, 0.50, implementation);
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

        [Theory]
        [InlineData(TouchpadWindowDragImplementation.SimulatedMouseDrag)]
        [InlineData(TouchpadWindowDragImplementation.ThreeFingerDrag)]
        [Trait("Category", "WindowsIntegration")]
        public void SimulatedMouseControllerSelectsClientTextWithoutMovingWindow(
            TouchpadWindowDragImplementation implementation)
        {
            RunWithSimulatedMouseTestWindow((form, textBox, window, controller) =>
            {
                RECT initialRectangle = window.Rectangle;
                Point initialCursor = (Point)textBox.Invoke(new Func<Point>(() => textBox.PointToScreen(new Point(8, textBox.ClientSize.Height / 2))));
                IntPtr hitWindow = NativeMethods.WindowFromPoint(new NativeMethods.Point(initialCursor.X, initialCursor.Y));
                Assert.Equal(form.Handle, NativeMethods.GetAncestor(hitWindow, NativeMethods.GA_ROOT));
                Cursor.Position = initialCursor;

                bool started = controller.Begin(window, 0.50, 0.50, implementation);
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

using GestureSign.Common.Input;
using GestureSign.Common.Log;
using GestureSign.Daemon.Native;
using ManagedWinapi.Windows;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowsInput;
using WindowRect = ManagedWinapi.Windows.RECT;

namespace GestureSign.Daemon.Triggers
{
    internal sealed class WindowDragController
    {
        private const int UpdateIntervalMilliseconds = 16;

        private readonly Stopwatch _updateStopwatch = new Stopwatch();
        private readonly InputSimulator _inputSimulator = new InputSimulator();
        private readonly WindowDragDiagnostics _diagnostics;
        private SystemWindow _window;
        private bool _active;
        private bool _failureLogged;
        private bool _simulatedLeftButtonDown;
        private bool _bringToForeground;
        private TouchpadWindowDragImplementation _implementation;
        private int _anchorX;
        private int _anchorY;
        private double _lastNormalizedX;
        private double _lastNormalizedY;
        private double _virtualCursorX;
        private double _virtualCursorY;
        private Point _lastCommandedCursor;
        private Point _pendingCursor;
        private bool _hasPendingPosition;

        internal string LastFailure { get; private set; }

        internal WindowDragController() : this(null)
        {
        }

        internal WindowDragController(WindowDragDiagnostics diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public bool Begin(SystemWindow window, double normalizedX, double normalizedY,
            TouchpadWindowDragImplementation implementation, bool bringToForeground = false)
        {
            return Begin(window, Cursor.Position, normalizedX, normalizedY, implementation, bringToForeground);
        }

        public bool Begin(SystemWindow window, Point cursor, double normalizedX, double normalizedY,
            TouchpadWindowDragImplementation implementation, bool bringToForeground = false)
        {
            End();
            if (!IsMovableWindow(window))
                return false;

            _window = window;
            _implementation = implementation;
            _bringToForeground = bringToForeground;
            _failureLogged = false;
            LastFailure = null;

            if (!PrepareWindowForDrag())
            {
                End();
                return false;
            }

            if (!TryRestoreCursor(cursor))
            {
                End();
                return false;
            }

            if (UsesSimulatedMouseDrag(implementation))
            {
                if (!TryStartSimulatedMouseDrag())
                {
                    End();
                    return false;
                }
            }
            else
            {
                ConfigureDirectWindowAnchor(window, cursor);
            }

            InitializeCursorTracking(normalizedX, normalizedY, cursor);
            _hasPendingPosition = implementation == TouchpadWindowDragImplementation.DirectSetWindowPos;
            _active = true;
            _updateStopwatch.Restart();

            return implementation == TouchpadWindowDragImplementation.DirectSetWindowPos
                ? MoveWindowToCursor(cursor)
                : true;
        }

        private bool TryRestoreCursor(Point cursor)
        {
            if (Cursor.Position == cursor)
                return true;

            if (!NativeMethods.SetCursorPos(cursor.X, cursor.Y))
            {
                LogFailureOnce("restore the captured cursor position", Marshal.GetLastWin32Error());
                return false;
            }

            Point actualCursor = Cursor.Position;
            if (actualCursor == cursor)
                return true;

            LogFailureOnce("restore the captured cursor position",
                $"Expected=({cursor.X},{cursor.Y}), Actual=({actualCursor.X},{actualCursor.Y})");
            return false;
        }

        public bool Update(double normalizedX, double normalizedY, double sensitivity)
        {
            if (_active)
                _diagnostics?.RecordControllerUpdate(Environment.TickCount64);

            if (!_active || !NativeMethods.IsWindow(_window.HWnd))
            {
                End();
                return false;
            }

            if (UsesSimulatedMouseDrag(_implementation) &&
                (!_simulatedLeftButtonDown || !IsLeftButtonDown()))
            {
                _simulatedLeftButtonDown = false;
                LogFailureOnce("maintain the simulated left-button hold", 0);
                End();
                return false;
            }

            Point actualCursor = Cursor.Position;
            bool cursorMovedOutsideController = Math.Abs(actualCursor.X - _lastCommandedCursor.X) > 1 ||
                                                Math.Abs(actualCursor.Y - _lastCommandedCursor.Y) > 1;
            if (cursorMovedOutsideController)
            {
                _virtualCursorX = actualCursor.X;
                _virtualCursorY = actualCursor.Y;
            }
            else
            {
                Screen currentScreen = Screen.FromPoint(_lastCommandedCursor) ?? Screen.PrimaryScreen;
                double deltaX = (normalizedX - _lastNormalizedX) * currentScreen.Bounds.Width * sensitivity;
                double deltaY = (normalizedY - _lastNormalizedY) * currentScreen.Bounds.Height * sensitivity;
                _virtualCursorX += deltaX;
                _virtualCursorY += deltaY;
            }

            _lastNormalizedX = normalizedX;
            _lastNormalizedY = normalizedY;
            ClampCursorToVirtualDesktop();

            Point desiredCursor = new Point((int)Math.Round(_virtualCursorX), (int)Math.Round(_virtualCursorY));
            if (desiredCursor != actualCursor && !NativeMethods.SetCursorPos(desiredCursor.X, desiredCursor.Y))
                LogFailureOnce("SetCursorPos", Marshal.GetLastWin32Error());

            _lastCommandedCursor = desiredCursor;

            if (UsesSimulatedMouseDrag(_implementation))
                return true;

            _pendingCursor = desiredCursor;
            _hasPendingPosition = true;

            if (_updateStopwatch.ElapsedMilliseconds < UpdateIntervalMilliseconds)
                return true;

            return FlushPendingPosition();
        }

        public bool Rebase(double normalizedX, double normalizedY)
        {
            return Rebase(_window, normalizedX, normalizedY);
        }

        public bool Rebase(SystemWindow window, double normalizedX, double normalizedY)
        {
            if (!_active)
                return false;

            Point cursor = Cursor.Position;
            if (!UsesSimulatedMouseDrag(_implementation))
                FlushPendingPosition();

            if (!IsMovableWindow(window))
            {
                End();
                return false;
            }

            _window = window;
            if (!PrepareWindowForDrag())
            {
                End();
                return false;
            }

            if (UsesSimulatedMouseDrag(_implementation))
            {
                if (!TryStartSimulatedMouseDrag())
                {
                    End();
                    return false;
                }
            }
            else
            {
                ConfigureDirectWindowAnchor(window, cursor);
            }

            InitializeCursorTracking(normalizedX, normalizedY, cursor);
            return true;
        }

        public void Pause()
        {
            if (UsesSimulatedMouseDrag(_implementation))
                ReleaseSimulatedLeftButton();
            else
                FlushPendingPosition();
        }

        public void End()
        {
            if (_active && _implementation == TouchpadWindowDragImplementation.DirectSetWindowPos)
                FlushPendingPosition();

            ReleaseSimulatedLeftButton();
            _active = false;
            _window = null;
            _bringToForeground = false;
            _hasPendingPosition = false;
            _anchorX = 0;
            _anchorY = 0;
            _updateStopwatch.Reset();
        }

        private void ConfigureDirectWindowAnchor(SystemWindow window, Point cursor)
        {
            WindowRect currentRectangle = window.Rectangle;
            WindowRect anchorRectangle = currentRectangle;
            bool maximizedAnchorSet = false;

            if ((window.Style & WindowStyleFlags.MAXIMIZE) != 0)
            {
                WindowRect normalRectangle = window.Position;
                if (normalRectangle.Width > 0 && normalRectangle.Height > 0 &&
                    currentRectangle.Width > 0 && currentRectangle.Height > 0)
                {
                    double horizontalRatio = Clamp((cursor.X - currentRectangle.Left) / (double)currentRectangle.Width, 0, 1);
                    double verticalRatio = Clamp((cursor.Y - currentRectangle.Top) / (double)currentRectangle.Height, 0, 1);
                    _anchorX = (int)Math.Round(horizontalRatio * normalRectangle.Width);
                    _anchorY = (int)Math.Round(verticalRatio * normalRectangle.Height);
                    anchorRectangle = normalRectangle;
                    maximizedAnchorSet = true;
                    window.RestoreWindow();
                }
            }

            if (!maximizedAnchorSet)
            {
                _anchorX = cursor.X - anchorRectangle.Left;
                _anchorY = cursor.Y - anchorRectangle.Top;
            }
        }

        private void InitializeCursorTracking(double normalizedX, double normalizedY, Point cursor)
        {
            _lastNormalizedX = normalizedX;
            _lastNormalizedY = normalizedY;
            _virtualCursorX = cursor.X;
            _virtualCursorY = cursor.Y;
            _lastCommandedCursor = cursor;
            _pendingCursor = cursor;
        }

        private bool TryStartSimulatedMouseDrag()
        {
            try
            {
                _inputSimulator.Mouse.LeftButtonDown();
                _simulatedLeftButtonDown = true;
                return true;
            }
            catch (Exception exception)
            {
                LogFailureOnce("send the simulated left-button down", exception.Message);
                TryReleaseLeftButtonAfterFailure();
                return false;
            }
        }

        private bool PrepareWindowForDrag()
        {
            if (UsesSimulatedMouseDrag(_implementation) && IsLeftButtonDown())
            {
                LogFailureOnce("start the simulated mouse drag because the left button is already down", 0);
                return false;
            }

            BringWindowToForegroundIfRequested();
            return true;
        }

        private void BringWindowToForegroundIfRequested()
        {
            if (!_bringToForeground || SystemWindow.ForegroundWindow.HWnd == _window.HWnd)
                return;

            if (_implementation == TouchpadWindowDragImplementation.DirectSetWindowPos)
            {
                if (TryActivateDirectDragWindow(out string failureDetail))
                    return;

                BringWindowToForegroundUsingLegacyFallback(failureDetail);
                return;
            }

            BringWindowToForegroundUsingLegacyFallback(null);
        }

        private bool TryActivateDirectDragWindow(out string failureDetail)
        {
            IntPtr targetWindow = _window.HWnd;
            bool initialSetForeground = NativeMethods.SetForegroundWindow(targetWindow);
            if (SystemWindow.ForegroundWindow.HWnd == targetWindow)
            {
                failureDetail = null;
                return true;
            }

            IntPtr foregroundWindow = SystemWindow.ForegroundWindow.HWnd;
            int currentThreadId = NativeMethods.GetCurrentThreadId();
            int foregroundThreadId = foregroundWindow == IntPtr.Zero
                ? 0
                : NativeMethods.GetWindowThreadProcessId(new HandleRef(this, foregroundWindow), out _);
            bool attachRequired = foregroundThreadId != 0 && foregroundThreadId != currentThreadId;
            bool attached = false;
            bool attachSucceeded = !attachRequired;
            bool detachSucceeded = true;
            bool broughtToTop = false;
            bool attachedSetForeground = false;
            int attachError = 0;
            int detachError = 0;

            // A background raw-input process is subject to the foreground lock. Temporarily
            // sharing the foreground queue grants the same activation path as a normal drag.
            try
            {
                if (attachRequired)
                {
                    attached = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
                    attachSucceeded = attached;
                    if (!attached)
                        attachError = Marshal.GetLastWin32Error();
                }

                if (attachSucceeded)
                {
                    broughtToTop = NativeMethods.BringWindowToTop(targetWindow);
                    attachedSetForeground = NativeMethods.SetForegroundWindow(targetWindow);
                }
            }
            finally
            {
                if (attached)
                {
                    detachSucceeded = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
                    if (!detachSucceeded)
                        detachError = Marshal.GetLastWin32Error();
                }
            }

            IntPtr actualForegroundWindow = SystemWindow.ForegroundWindow.HWnd;
            if (detachSucceeded && actualForegroundWindow == targetWindow)
            {
                failureDetail = null;
                return true;
            }

            failureDetail = $"InitialSetForeground={initialSetForeground}, " +
                            $"ForegroundBeforeAttach=0x{foregroundWindow.ToInt64():X}, " +
                            $"CurrentThread={currentThreadId}, ForegroundThread={foregroundThreadId}, " +
                            $"AttachRequired={attachRequired}, AttachSucceeded={attachSucceeded}, AttachError={attachError}, " +
                            $"BringWindowToTop={broughtToTop}, AttachedSetForeground={attachedSetForeground}, " +
                            $"DetachSucceeded={detachSucceeded}, DetachError={detachError}, " +
                            $"ActualForeground=0x{actualForegroundWindow.ToInt64():X}";
            return false;
        }

        private void BringWindowToForegroundUsingLegacyFallback(string priorFailureDetail)
        {
            if (NativeMethods.SetForegroundWindow(_window.HWnd))
                return;

            WindowPositionFlags flags = WindowPositionFlags.NoMove |
                                        WindowPositionFlags.NoSize |
                                        WindowPositionFlags.ShowWindow |
                                        WindowPositionFlags.AsyncWindowPosition;
            if (!WindowPositionInterop.SetWindowPosition(_window.HWnd, IntPtr.Zero, 0, 0, 0, 0, flags))
            {
                int error = Marshal.GetLastWin32Error();
                string detail = priorFailureDetail == null
                    ? $"Win32Error={error}"
                    : $"{priorFailureDetail}, LegacySetWindowPosError={error}";
                LogFailureOnce("bring the window to the foreground", detail);
                return;
            }

            bool finalSetForeground = NativeMethods.SetForegroundWindow(_window.HWnd);
            if (priorFailureDetail != null && SystemWindow.ForegroundWindow.HWnd != _window.HWnd)
            {
                LogFailureOnce("bring the window to the foreground",
                    $"{priorFailureDetail}, LegacySetWindowPosQueued=True, " +
                    $"LegacySetForeground={finalSetForeground}, " +
                    $"ActualForeground=0x{SystemWindow.ForegroundWindow.HWnd.ToInt64():X}");
            }
        }

        private static bool IsLeftButtonDown()
        {
            return (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
        }

        private static bool UsesSimulatedMouseDrag(TouchpadWindowDragImplementation implementation)
        {
            return implementation == TouchpadWindowDragImplementation.SimulatedMouseDrag ||
                   implementation == TouchpadWindowDragImplementation.ThreeFingerDrag;
        }

        private void ReleaseSimulatedLeftButton()
        {
            if (!_simulatedLeftButtonDown)
                return;

            try
            {
                _inputSimulator.Mouse.LeftButtonUp();
            }
            catch (Exception exception)
            {
                LogFailureOnce("send the simulated left-button up", exception.Message);
            }
            finally
            {
                _simulatedLeftButtonDown = false;
            }
        }

        private void TryReleaseLeftButtonAfterFailure()
        {
            try
            {
                _inputSimulator.Mouse.LeftButtonUp();
            }
            catch
            {
            }
            _simulatedLeftButtonDown = false;
        }

        private bool FlushPendingPosition()
        {
            if (!_active || !_hasPendingPosition)
                return _active;

            ObserveWindowPosition();
            _hasPendingPosition = false;
            _updateStopwatch.Restart();
            return MoveWindowToCursor(_pendingCursor);
        }

        private bool MoveWindowToCursor(Point cursor)
        {
            WindowPositionFlags flags = WindowPositionFlags.NoSize |
                                        WindowPositionFlags.NoZOrder |
                                        WindowPositionFlags.NoActivate |
                                        WindowPositionFlags.NoOwnerZOrder |
                                        WindowPositionFlags.AsyncWindowPosition;
            int requestedLeft = cursor.X - _anchorX;
            int requestedTop = cursor.Y - _anchorY;
            long callStartedAt = Stopwatch.GetTimestamp();
            bool moved = WindowPositionInterop.SetWindowPosition(_window.HWnd, IntPtr.Zero,
                requestedLeft, requestedTop, 0, 0, flags);
            int errorCode = moved ? 0 : Marshal.GetLastWin32Error();
            long callTicks = Math.Max(0, Stopwatch.GetTimestamp() - callStartedAt);
            long callMicroseconds = (long)(callTicks * (1_000_000d / Stopwatch.Frequency));
            _diagnostics?.RecordWindowMoveRequest(Environment.TickCount64,
                requestedLeft, requestedTop, moved, callMicroseconds);
            if (!moved)
                LogFailureOnce("SetWindowPos", errorCode);
            return moved;
        }

        private void ObserveWindowPosition()
        {
            if (_diagnostics == null || _window == null)
                return;

            try
            {
                WindowRect rectangle = _window.Rectangle;
                _diagnostics.ObserveWindowPosition(Environment.TickCount64,
                    rectangle.Left, rectangle.Top);
            }
            catch
            {
                // Diagnostics must never interrupt an active drag.
            }
        }

        private void ClampCursorToVirtualDesktop()
        {
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            _virtualCursorX = Clamp(_virtualCursorX, virtualScreen.Left, virtualScreen.Right - 1);
            _virtualCursorY = Clamp(_virtualCursorY, virtualScreen.Top, virtualScreen.Bottom - 1);
        }

        private void LogFailureOnce(string operation, int errorCode)
        {
            LogFailureOnce(operation, $"Win32Error={errorCode}");
        }

        private void LogFailureOnce(string operation, string detail)
        {
            if (_failureLogged)
                return;

            _failureLogged = true;
            _diagnostics?.RecordControllerFailure(Environment.TickCount64, operation);
            string handle = _window == null ? "0" : _window.HWnd.ToInt64().ToString("X");
            LastFailure = $"Touchpad window drag {operation} failed. HWND=0x{handle}, {detail}.";
            Logging.LogMessage(LastFailure);
        }

        private static bool IsMovableWindow(SystemWindow window)
        {
            return window != null &&
                   window.HWnd != IntPtr.Zero &&
                   window.HWnd != SystemWindow.DesktopWindow.HWnd &&
                   window.HWnd != SystemWindow.ShellWindow.HWnd &&
                   window.Visible &&
                   window.Enabled &&
                   NativeMethods.IsWindow(window.HWnd);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}

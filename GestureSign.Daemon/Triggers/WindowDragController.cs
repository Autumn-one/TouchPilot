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
        private SystemWindow _window;
        private bool _active;
        private bool _failureLogged;
        private bool _simulatedLeftButtonDown;
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

        public bool Begin(SystemWindow window, double normalizedX, double normalizedY,
            TouchpadWindowDragImplementation implementation)
        {
            End();
            if (!IsMovableWindow(window))
                return false;

            _window = window;
            Point cursor = Cursor.Position;
            _implementation = implementation;
            _failureLogged = false;
            LastFailure = null;

            if (implementation == TouchpadWindowDragImplementation.SimulatedMouseDrag)
            {
                if (!TryStartSimulatedMouseDrag())
                {
                    _window = null;
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

        public bool Update(double normalizedX, double normalizedY, double sensitivity)
        {
            if (!_active || !NativeMethods.IsWindow(_window.HWnd))
            {
                End();
                return false;
            }

            if (_implementation == TouchpadWindowDragImplementation.SimulatedMouseDrag &&
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

            if (_implementation == TouchpadWindowDragImplementation.SimulatedMouseDrag)
                return true;

            _pendingCursor = desiredCursor;
            _hasPendingPosition = true;

            if (_updateStopwatch.ElapsedMilliseconds < UpdateIntervalMilliseconds)
                return true;

            return FlushPendingPosition();
        }

        public bool Rebase(double normalizedX, double normalizedY)
        {
            if (!_active)
                return false;

            Point cursor = Cursor.Position;
            if (_implementation == TouchpadWindowDragImplementation.SimulatedMouseDrag)
            {
                if (!TryStartSimulatedMouseDrag())
                {
                    End();
                    return false;
                }
            }
            else
            {
                FlushPendingPosition();
                ConfigureDirectWindowAnchor(_window, cursor);
            }

            InitializeCursorTracking(normalizedX, normalizedY, cursor);
            return true;
        }

        public void Pause()
        {
            if (_implementation == TouchpadWindowDragImplementation.SimulatedMouseDrag)
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
            if (IsLeftButtonDown())
            {
                LogFailureOnce("start the simulated mouse drag because the left button is already down", 0);
                return false;
            }

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

        private static bool IsLeftButtonDown()
        {
            return (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
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

            _hasPendingPosition = false;
            _updateStopwatch.Restart();
            return MoveWindowToCursor(_pendingCursor);
        }

        private bool MoveWindowToCursor(Point cursor)
        {
            NativeMethods.SWP flags = NativeMethods.SWP.SWP_NOSIZE |
                                      NativeMethods.SWP.SWP_NOZORDER |
                                      NativeMethods.SWP.SWP_NOACTIVATE |
                                      NativeMethods.SWP.SWP_NOOWNERZORDER |
                                      NativeMethods.SWP.SWP_ASYNCWINDOWPOS;
            bool moved = NativeMethods.SetWindowPos(_window.HWnd, IntPtr.Zero, cursor.X - _anchorX, cursor.Y - _anchorY, 0, 0, flags);
            if (!moved)
                LogFailureOnce("SetWindowPos", Marshal.GetLastWin32Error());
            return moved;
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

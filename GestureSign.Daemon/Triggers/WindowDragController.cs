using GestureSign.Common.Log;
using GestureSign.Daemon.Native;
using ManagedWinapi.Windows;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowRect = ManagedWinapi.Windows.RECT;

namespace GestureSign.Daemon.Triggers
{
    internal sealed class WindowDragController
    {
        private const int UpdateIntervalMilliseconds = 16;

        private readonly Stopwatch _updateStopwatch = new Stopwatch();
        private SystemWindow _window;
        private bool _active;
        private bool _failureLogged;
        private int _anchorX;
        private int _anchorY;
        private double _lastNormalizedX;
        private double _lastNormalizedY;
        private double _virtualCursorX;
        private double _virtualCursorY;
        private Point _lastCommandedCursor;
        private Point _pendingCursor;
        private bool _hasPendingPosition;

        public bool Begin(SystemWindow window, double normalizedX, double normalizedY)
        {
            End();
            if (!IsMovableWindow(window))
                return false;

            _window = window;
            Point cursor = Cursor.Position;
            WindowRect currentRectangle = window.Rectangle;
            WindowRect anchorRectangle = currentRectangle;
            bool maximizedAnchorSet = false;

            if ((window.Style & WindowStyleFlags.MAXIMIZE) != 0)
            {
                WindowRect normalRectangle = window.Position;
                if (normalRectangle.Width > 0 && normalRectangle.Height > 0 && currentRectangle.Width > 0 && currentRectangle.Height > 0)
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

            _lastNormalizedX = normalizedX;
            _lastNormalizedY = normalizedY;
            _virtualCursorX = cursor.X;
            _virtualCursorY = cursor.Y;
            _lastCommandedCursor = cursor;
            _pendingCursor = cursor;
            _hasPendingPosition = true;
            _failureLogged = false;
            _active = true;
            _updateStopwatch.Restart();

            return MoveWindowToCursor(cursor);
        }

        public bool Update(double normalizedX, double normalizedY, double sensitivity)
        {
            if (!_active || !NativeMethods.IsWindow(_window.HWnd))
            {
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
            _pendingCursor = desiredCursor;
            _hasPendingPosition = true;

            if (_updateStopwatch.ElapsedMilliseconds < UpdateIntervalMilliseconds)
                return true;

            return FlushPendingPosition();
        }

        public void Rebase(double normalizedX, double normalizedY)
        {
            if (!_active)
                return;

            FlushPendingPosition();
            Point cursor = Cursor.Position;
            _lastNormalizedX = normalizedX;
            _lastNormalizedY = normalizedY;
            _virtualCursorX = cursor.X;
            _virtualCursorY = cursor.Y;
            _lastCommandedCursor = cursor;
        }

        public void Pause()
        {
            FlushPendingPosition();
        }

        public void End()
        {
            if (_active)
                FlushPendingPosition();

            _active = false;
            _window = null;
            _hasPendingPosition = false;
            _anchorX = 0;
            _anchorY = 0;
            _updateStopwatch.Reset();
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
            if (_failureLogged)
                return;

            _failureLogged = true;
            string handle = _window == null ? "0" : _window.HWnd.ToInt64().ToString("X");
            Logging.LogMessage($"Touchpad window drag {operation} failed. HWND=0x{handle}, Win32Error={errorCode}.");
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

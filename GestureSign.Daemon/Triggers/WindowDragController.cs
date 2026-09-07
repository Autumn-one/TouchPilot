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
        private static readonly TimeSpan DirectMoveFlushTimeout = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan DirectMoveStopTimeout = TimeSpan.FromMilliseconds(100);

        private readonly InputSimulator _inputSimulator = new InputSimulator();
        private readonly WindowDragDiagnostics _diagnostics;
        private readonly NativeWindowMoveLoop _nativeMoveLoop = new NativeWindowMoveLoop();
        private readonly WindowDragTrajectory _directTrajectory = new WindowDragTrajectory();
        private WindowDragMotionPump _directMotionPump;
        private SystemWindow _window;
        private bool _active;
        private bool _failureLogged;
        private bool _simulatedLeftButtonDown;
        private bool _nativeMoveLoopFallback;
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
        private Point? _externalCursor;
        private bool _settingCursor;
        private bool _motionPaused;

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
            long beginStartedAt = StartDiagnosticTimer();
            try
            {
                return BeginCore(window, cursor, normalizedX, normalizedY,
                    implementation, bringToForeground);
            }
            finally
            {
                if (beginStartedAt != 0)
                {
                    _diagnostics.RecordControllerBeginDuration(Environment.TickCount64,
                        GetElapsedMicroseconds(beginStartedAt));
                }
            }
        }

        private bool BeginCore(SystemWindow window, Point cursor,
            double normalizedX,
            double normalizedY,
            TouchpadWindowDragImplementation implementation,
            bool bringToForeground)
        {
            long stageStartedAt = StartDiagnosticTimer();
            End();
            RecordControllerBeginStage("end-previous", stageStartedAt);

            stageStartedAt = StartDiagnosticTimer();
            bool movableWindow = IsMovableWindow(window);
            RecordControllerBeginStage("validate-target", stageStartedAt);
            if (!movableWindow)
                return false;

            _window = window;
            _implementation = implementation;
            _bringToForeground = bringToForeground;
            _nativeMoveLoopFallback = false;
            _failureLogged = false;
            LastFailure = null;

            stageStartedAt = StartDiagnosticTimer();
            bool windowPrepared = PrepareWindowForDrag();
            RecordControllerBeginStage("prepare-window", stageStartedAt);
            if (!windowPrepared)
            {
                End();
                return false;
            }

            stageStartedAt = StartDiagnosticTimer();
            bool cursorRestored = TryRestoreCursor(cursor);
            RecordControllerBeginStage("restore-cursor", stageStartedAt);
            if (!cursorRestored)
            {
                End();
                return false;
            }

            stageStartedAt = StartDiagnosticTimer();
            if (UsesSimulatedMouseDrag(implementation))
            {
                if (!TryStartSimulatedMouseDrag())
                {
                    RecordControllerBeginStage("start-simulated-drag", stageStartedAt);
                    End();
                    return false;
                }
                RecordControllerBeginStage("start-simulated-drag", stageStartedAt);
            }
            else
            {
                ConfigureDirectWindowAnchor(window, cursor);
                RecordControllerBeginStage("configure-anchor", stageStartedAt);
            }

            stageStartedAt = StartDiagnosticTimer();
            InitializeCursorTracking(normalizedX, normalizedY, cursor);
            _hasPendingPosition = UsesDirectWindowPositioning();
            _active = true;
            RecordControllerBeginStage("initialize-tracking", stageStartedAt);

            if (implementation == TouchpadWindowDragImplementation.NativeMoveLoop)
            {
                stageStartedAt = StartDiagnosticTimer();
                bool nativeStarted = _nativeMoveLoop.TryStart(_window.HWnd);
                _diagnostics?.RecordNativeMoveLoopStart(nativeStarted,
                    _nativeMoveLoop.LastFailure);
                RecordControllerBeginStage("start-native-move-loop", stageStartedAt);
                if (nativeStarted)
                {
                    InitializeCursorTracking(normalizedX, normalizedY, Cursor.Position);
                    return true;
                }

                _nativeMoveLoopFallback = true;
                _hasPendingPosition = true;
                return FlushPendingPosition(true);
            }

            if (!UsesDirectWindowPositioning())
                return true;

            stageStartedAt = StartDiagnosticTimer();
            bool moved = FlushPendingPosition(true);
            RecordControllerBeginStage("initial-window-move", stageStartedAt);
            return moved;
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
            long updateStartedAt = StartDiagnosticTimer();
            try
            {
                return UpdateCore(normalizedX, normalizedY, sensitivity);
            }
            finally
            {
                if (updateStartedAt != 0)
                {
                    _diagnostics.RecordControllerUpdateDuration(Environment.TickCount64,
                        GetElapsedMicroseconds(updateStartedAt));
                }
            }
        }

        private bool UpdateCore(double normalizedX, double normalizedY, double sensitivity)
        {
            if (_active)
                _diagnostics?.RecordControllerUpdate(Environment.TickCount64);

            if (!_active || !NativeMethods.IsWindow(_window.HWnd))
            {
                End();
                return false;
            }

            if (!DrainDirectMotionResults())
                return false;

            if (UsesSimulatedMouseDrag(_implementation) &&
                (!_simulatedLeftButtonDown || !IsLeftButtonDown()))
            {
                _simulatedLeftButtonDown = false;
                LogFailureOnce("maintain the simulated left-button hold", 0);
                End();
                return false;
            }

            Point actualCursor = Cursor.Position;
            if (_implementation == TouchpadWindowDragImplementation.NativeMoveLoop &&
                !_nativeMoveLoopFallback && !_nativeMoveLoop.IsActive)
            {
                ConfigureDirectWindowAnchor(_window, actualCursor);
                InitializeCursorTracking(normalizedX, normalizedY, actualCursor);
                _nativeMoveLoopFallback = true;
                _diagnostics?.RecordNativeMoveLoopStart(false,
                    "move-loop-ended-during-update");
            }
            Point desiredCursor;
            if (UsesDirectWindowPositioning())
            {
                double timestamp = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                if (_externalCursor.HasValue)
                {
                    _directTrajectory.Rebase(normalizedX, normalizedY,
                        _externalCursor.Value, timestamp);
                    _externalCursor = null;
                }
                desiredCursor = _directTrajectory.Update(normalizedX, normalizedY,
                    sensitivity, timestamp, SystemInformation.VirtualScreen);
            }
            else
            {
                desiredCursor = UpdateLegacyCursor(normalizedX, normalizedY, sensitivity, actualCursor);
            }

            if (desiredCursor != actualCursor)
            {
                _settingCursor = true;
                try
                {
                    if (!MoveCursor(desiredCursor))
                        LogFailureOnce("move the cursor", Marshal.GetLastWin32Error());
                }
                finally
                {
                    _settingCursor = false;
                }
            }

            _lastCommandedCursor = desiredCursor;

            if (UsesSimulatedMouseDrag(_implementation))
                return true;

            if (_implementation == TouchpadWindowDragImplementation.NativeMoveLoop &&
                !_nativeMoveLoopFallback)
            {
                if (_nativeMoveLoop.IsActive)
                    return true;

                ConfigureDirectWindowAnchor(_window, desiredCursor);
                InitializeCursorTracking(normalizedX, normalizedY, desiredCursor);
                _nativeMoveLoopFallback = true;
                _diagnostics?.RecordNativeMoveLoopStart(false,
                    "move-loop-ended-during-cursor-update");
            }

            _pendingCursor = desiredCursor;
            _hasPendingPosition = true;
            return FlushPendingPosition();
        }

        internal void ObserveExternalCursor(Point cursor)
        {
            if (_active && !_motionPaused && !_settingCursor &&
                UsesDirectWindowPositioning() && cursor != _lastCommandedCursor)
                _externalCursor = cursor;
        }

        private Point UpdateLegacyCursor(double normalizedX, double normalizedY,
            double sensitivity, Point actualCursor)
        {
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

            return new Point((int)Math.Round(_virtualCursorX), (int)Math.Round(_virtualCursorY));
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
            if (_implementation == TouchpadWindowDragImplementation.NativeMoveLoop &&
                !_nativeMoveLoopFallback)
            {
                _nativeMoveLoop.Commit();
            }
            else if (!UsesSimulatedMouseDrag(_implementation))
                FlushPendingPosition(true);

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
            if (_implementation == TouchpadWindowDragImplementation.NativeMoveLoop)
            {
                _nativeMoveLoopFallback = !_nativeMoveLoop.TryStart(_window.HWnd);
                _diagnostics?.RecordNativeMoveLoopStart(!_nativeMoveLoopFallback,
                    _nativeMoveLoop.LastFailure);
                if (!_nativeMoveLoopFallback)
                {
                    InitializeCursorTracking(normalizedX, normalizedY, Cursor.Position);
                    return true;
                }

                _hasPendingPosition = true;
                return FlushPendingPosition(true);
            }
            return true;
        }

        public void Pause()
        {
            _motionPaused = true;
            _externalCursor = null;
            if (UsesSimulatedMouseDrag(_implementation))
                ReleaseSimulatedLeftButton();
            else if (_implementation == TouchpadWindowDragImplementation.NativeMoveLoop &&
                     !_nativeMoveLoopFallback)
            {
                if (!_nativeMoveLoop.Commit())
                    LogFailureOnce("exit the native window move loop", 0);
            }
            else
                FlushPendingPosition(true);
        }

        public void End()
        {
            if (_active && UsesDirectWindowPositioning())
                FlushPendingPosition(true);

            if (_implementation == TouchpadWindowDragImplementation.NativeMoveLoop &&
                !_nativeMoveLoop.Commit())
            {
                LogFailureOnce("exit the native window move loop", 0);
            }

            StopDirectMotionPump();

            ReleaseSimulatedLeftButton();
            _active = false;
            _externalCursor = null;
            _motionPaused = false;
            _window = null;
            _bringToForeground = false;
            _nativeMoveLoopFallback = false;
            _hasPendingPosition = false;
            _anchorX = 0;
            _anchorY = 0;
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
            Screen screen = Screen.FromPoint(cursor) ?? Screen.PrimaryScreen;
            _directTrajectory.Begin(normalizedX, normalizedY, cursor, screen.Bounds.Size,
                Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            _externalCursor = null;
            _motionPaused = false;
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
            bool nativeMoveLoop = _implementation == TouchpadWindowDragImplementation.NativeMoveLoop;
            if ((!_bringToForeground && !nativeMoveLoop) ||
                SystemWindow.ForegroundWindow.HWnd == _window.HWnd)
                return;

            if (UsesDirectWindowPositioning() || nativeMoveLoop)
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

        private bool FlushPendingPosition(bool waitForCompletion = false)
        {
            if (!_active || !_hasPendingPosition)
                return _active && DrainDirectMotionResults();

            bool alreadyPositioned = ObservePendingWindowPosition();
            _hasPendingPosition = false;
            if (UsesDirectWindowPositioning())
            {
                if (alreadyPositioned && !(_directMotionPump?.HasPendingMoves ?? false))
                {
                    _diagnostics?.RecordUnchangedWindowMoveTarget();
                    return DrainDirectMotionResults();
                }
                return PublishDirectWindowPosition(_pendingCursor, waitForCompletion);
            }
            return MoveWindowToCursor(_pendingCursor);
        }

        private bool PublishDirectWindowPosition(Point cursor, bool waitForCompletion)
        {
            int targetThreadId = NativeMethods.GetWindowThreadProcessId(
                new HandleRef(this, _window.HWnd), out _);
            bool requiresAsynchronousPositioning = targetThreadId == NativeMethods.GetCurrentThreadId();
            var request = new WindowDragMotionRequest(_window.HWnd,
                cursor.X - _anchorX, cursor.Y - _anchorY, requiresAsynchronousPositioning);

            _directMotionPump ??= new WindowDragMotionPump();
            if (!_directMotionPump.Publish(request))
            {
                StopDirectMotionPump();
                return MoveWindowToCursor(cursor);
            }
            _diagnostics?.RecordWindowMoveTargetPublished();

            if (waitForCompletion && !_directMotionPump.Flush(DirectMoveFlushTimeout))
            {
                StopDirectMotionPump();
                return MoveWindowToCursor(cursor);
            }

            return DrainDirectMotionResults();
        }

        private bool DrainDirectMotionResults()
        {
            if (_directMotionPump == null)
                return true;

            bool succeeded = true;
            while (_directMotionPump.TryTakeResult(out WindowDragMotionResult result))
            {
                _diagnostics?.RecordWindowMoveRequest(result.TimestampMilliseconds,
                    result.RequestedLeft, result.RequestedTop, result.Succeeded,
                    result.CallMicroseconds, result.UsedAsynchronousFallback,
                    result.CompositionWaitMicroseconds, result.QueueWaitMicroseconds);
                if (!result.Succeeded)
                {
                    succeeded = false;
                    LogFailureOnce("SetWindowPos", result.ErrorCode);
                }
            }
            return succeeded;
        }

        private void StopDirectMotionPump()
        {
            WindowDragMotionPump pump = _directMotionPump;
            if (pump == null)
                return;

            _directMotionPump = null;
            pump.Stop(DirectMoveStopTimeout);
            _diagnostics?.RecordWindowMoveTargetsCoalesced(pump.CoalescedTargets);
            while (pump.TryTakeResult(out WindowDragMotionResult result))
            {
                _diagnostics?.RecordWindowMoveRequest(result.TimestampMilliseconds,
                    result.RequestedLeft, result.RequestedTop, result.Succeeded,
                    result.CallMicroseconds, result.UsedAsynchronousFallback,
                    result.CompositionWaitMicroseconds, result.QueueWaitMicroseconds);
                if (!result.Succeeded)
                    LogFailureOnce("SetWindowPos", result.ErrorCode);
            }
            pump.Dispose();
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
            long callMicroseconds = GetElapsedMicroseconds(callStartedAt);
            _diagnostics?.RecordWindowMoveTargetPublished();
            _diagnostics?.RecordWindowMoveRequest(Environment.TickCount64,
                requestedLeft, requestedTop, moved, callMicroseconds, true);
            if (!moved)
                LogFailureOnce("SetWindowPos", errorCode);
            return moved;
        }

        private bool UsesDirectWindowPositioning()
        {
            return _implementation == TouchpadWindowDragImplementation.DirectSetWindowPos ||
                   _implementation == TouchpadWindowDragImplementation.ThreeFingerWindowDrag ||
                   (_implementation == TouchpadWindowDragImplementation.NativeMoveLoop &&
                    _nativeMoveLoopFallback);
        }

        private long StartDiagnosticTimer()
        {
            return _diagnostics == null ? 0 : Stopwatch.GetTimestamp();
        }

        private void RecordControllerBeginStage(string stage, long startedAt)
        {
            if (startedAt == 0)
                return;

            _diagnostics.RecordControllerBeginStage(Environment.TickCount64,
                stage, GetElapsedMicroseconds(startedAt));
        }

        private static long GetElapsedMicroseconds(long startedAt)
        {
            long elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - startedAt);
            return (long)(elapsedTicks * (1_000_000d / Stopwatch.Frequency));
        }

        private bool ObservePendingWindowPosition()
        {
            if (_window == null)
                return false;

            try
            {
                WindowRect rectangle = _window.Rectangle;
                _diagnostics?.ObserveWindowPosition(Environment.TickCount64,
                    rectangle.Left, rectangle.Top);
                return rectangle.Left == _pendingCursor.X - _anchorX &&
                       rectangle.Top == _pendingCursor.Y - _anchorY;
            }
            catch
            {
                // Failed observation keeps the established positioning path available.
                return false;
            }
        }

        private void ClampCursorToVirtualDesktop()
        {
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            _virtualCursorX = Clamp(_virtualCursorX, virtualScreen.Left, virtualScreen.Right - 1);
            _virtualCursorY = Clamp(_virtualCursorY, virtualScreen.Top, virtualScreen.Bottom - 1);
        }

        private bool MoveCursor(Point cursor)
        {
            if (_implementation != TouchpadWindowDragImplementation.NativeMoveLoop ||
                _nativeMoveLoopFallback)
            {
                return NativeMethods.SetCursorPos(cursor.X, cursor.Y);
            }

            try
            {
                Rectangle virtualScreen = SystemInformation.VirtualScreen;
                double absoluteX = (cursor.X - virtualScreen.Left) * 65535d /
                                   Math.Max(1, virtualScreen.Width - 1);
                double absoluteY = (cursor.Y - virtualScreen.Top) * 65535d /
                                   Math.Max(1, virtualScreen.Height - 1);
                _inputSimulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absoluteX, absoluteY);
                return true;
            }
            catch
            {
                return false;
            }
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

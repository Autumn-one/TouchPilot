using GestureSign.Daemon.Native;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace GestureSign.Daemon.Triggers
{
    internal sealed class NativeWindowMoveLoop
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromMilliseconds(15);
        private static readonly TimeSpan CommitTimeout = TimeSpan.FromMilliseconds(75);
        private static readonly TimeSpan InitializationStepTimeout = TimeSpan.FromMilliseconds(50);

        private IntPtr _windowHandle;
        private Thread _senderThread;

        internal bool IsActive => _senderThread?.IsAlive == true &&
                                  !WindowPositionInterop.IsHungAppWindow(_windowHandle);
        internal string LastFailure { get; private set; }

        internal bool TryStart(IntPtr windowHandle)
        {
            LastFailure = null;
            if (windowHandle == IntPtr.Zero)
            {
                LastFailure = "invalid-window";
                return false;
            }

            Commit();
            int windowThreadId = NativeMethods.GetWindowThreadProcessId(
                new HandleRef(this, windowHandle), out _);
            if (windowThreadId == 0 || windowThreadId == NativeMethods.GetCurrentThreadId())
            {
                LastFailure = windowThreadId == 0 ? "missing-window-thread" : "same-thread-window";
                return false;
            }

            bool started = TryStartWithMessage(windowHandle, windowThreadId,
                NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MOVE, 0,
                out Thread senderThread, out string failureDetail);

            if (!started)
            {
                Post(windowHandle, NativeMethods.WM_CANCELMODE, 0, 0);
                LastFailure = $"move-loop-not-entered ({failureDetail})";
                return false;
            }

            _windowHandle = windowHandle;
            _senderThread = senderThread;
            if (!InitializeKeyboardMoveLoop(windowHandle))
            {
                LastFailure = "keyboard-move-loop-initialization-failed";
                Commit();
                return false;
            }
            return true;
        }

        internal bool Commit()
        {
            IntPtr windowHandle = _windowHandle;
            Thread senderThread = _senderThread;
            _windowHandle = IntPtr.Zero;
            _senderThread = null;

            if (windowHandle == IntPtr.Zero || senderThread?.IsAlive != true)
            {
                return true;
            }

            bool enterPosted = Post(windowHandle, NativeMethods.WM_KEYDOWN,
                                   NativeMethods.VK_RETURN, 0) &&
                               Post(windowHandle, NativeMethods.WM_KEYUP,
                                   NativeMethods.VK_RETURN, 0);
            if (enterPosted && WaitForMoveLoopExit(senderThread, CommitTimeout))
            {
                senderThread?.Join(CommitTimeout);
                return true;
            }

            Post(windowHandle, NativeMethods.WM_CANCELMODE, 0, 0);
            bool stopped = WaitForMoveLoopExit(senderThread, CommitTimeout);
            senderThread?.Join(CommitTimeout);
            return stopped;
        }

        private static bool TryStartWithMessage(IntPtr windowHandle, int windowThreadId,
            int message, int wParam, int lParam, out Thread senderThread,
            out string failureDetail)
        {
            bool attachSucceeded = false;
            int attachError = 0;
            bool sendReturned = false;
            long sendResult = 0;
            int sendError = 0;
            senderThread = new Thread(() =>
            {
                int senderThreadId = NativeMethods.GetCurrentThreadId();
                bool attached = NativeMethods.AttachThreadInput(senderThreadId,
                    windowThreadId, true);
                attachSucceeded = attached;
                if (!attached)
                    attachError = Marshal.GetLastWin32Error();
                try
                {
                    sendResult = WindowPositionInterop.SendWindowMessage(windowHandle,
                        (uint)message, new IntPtr(wParam), new IntPtr(lParam)).ToInt64();
                    sendError = Marshal.GetLastWin32Error();
                    sendReturned = true;
                }
                finally
                {
                    if (attached)
                    {
                        NativeMethods.AttachThreadInput(senderThreadId,
                            windowThreadId, false);
                    }
                }
            })
            {
                IsBackground = true,
                Name = "TouchPilot native window move-loop sender"
            };
            senderThread.Start();

            var stopwatch = Stopwatch.StartNew();
            do
            {
                if (!senderThread.IsAlive)
                {
                    failureDetail = $"attach={attachSucceeded} attachError={attachError} " +
                                    $"sendReturned={sendReturned} sendResult={sendResult} " +
                                    $"sendError={sendError}";
                    return false;
                }
                Thread.Sleep(1);
            }
            while (stopwatch.Elapsed < StartTimeout);

            if (senderThread.IsAlive && !WindowPositionInterop.IsHungAppWindow(windowHandle))
            {
                failureDetail = null;
                return true;
            }

            Post(windowHandle, NativeMethods.WM_CANCELMODE, 0, 0);
            senderThread.Join(CommitTimeout);
            failureDetail = $"attach={attachSucceeded} attachError={attachError} " +
                            $"sendReturned={sendReturned} sendResult={sendResult} " +
                            $"sendError={sendError} hung=" +
                            WindowPositionInterop.IsHungAppWindow(windowHandle);
            return false;
        }

        private static bool WaitForMoveLoopExit(Thread senderThread, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            do
            {
                if (senderThread?.IsAlive != true)
                    return true;
                Thread.Sleep(1);
            }
            while (stopwatch.Elapsed < timeout);

            return senderThread?.IsAlive != true;
        }

        private static bool InitializeKeyboardMoveLoop(IntPtr windowHandle)
        {
            if (!WindowPositionInterop.GetWindowRectangle(windowHandle,
                    out WindowPositionInterop.NativeWindowRectangle initialRectangle))
            {
                return false;
            }

            int reverseDirection = NativeMethods.VK_LEFT;
            PostKey(windowHandle, NativeMethods.VK_RIGHT);
            if (!WaitForWindowPosition(windowHandle, initialRectangle.Left,
                    initialRectangle.Top, equal: false, InitializationStepTimeout))
            {
                reverseDirection = NativeMethods.VK_RIGHT;
                PostKey(windowHandle, NativeMethods.VK_LEFT);
                if (!WaitForWindowPosition(windowHandle, initialRectangle.Left,
                        initialRectangle.Top, equal: false, InitializationStepTimeout))
                {
                    return false;
                }
            }

            PostKey(windowHandle, reverseDirection);
            return WaitForWindowPosition(windowHandle, initialRectangle.Left,
                initialRectangle.Top, equal: true, InitializationStepTimeout);
        }

        private static void PostKey(IntPtr windowHandle, int virtualKey)
        {
            Post(windowHandle, NativeMethods.WM_KEYDOWN, virtualKey, 0);
            Post(windowHandle, NativeMethods.WM_KEYUP, virtualKey, 0);
        }

        private static bool WaitForWindowPosition(IntPtr windowHandle, int left, int top,
            bool equal, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            do
            {
                if (!WindowPositionInterop.GetWindowRectangle(windowHandle,
                        out WindowPositionInterop.NativeWindowRectangle rectangle))
                {
                    return false;
                }

                bool positionEquals = rectangle.Left == left && rectangle.Top == top;
                if (positionEquals == equal)
                    return true;
                Thread.Sleep(1);
            }
            while (stopwatch.Elapsed < timeout);

            return false;
        }

        private static bool Post(IntPtr windowHandle, int message, int wParam, int lParam)
        {
            return WindowPositionInterop.PostWindowMessage(windowHandle, (uint)message,
                new IntPtr(wParam), new IntPtr(lParam));
        }
    }
}

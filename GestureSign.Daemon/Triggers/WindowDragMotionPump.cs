using GestureSign.Daemon.Native;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace GestureSign.Daemon.Triggers
{
    internal readonly struct WindowDragMotionRequest
    {
        internal WindowDragMotionRequest(IntPtr windowHandle, int left, int top,
            bool requiresAsynchronousPositioning)
        {
            WindowHandle = windowHandle;
            Left = left;
            Top = top;
            RequiresAsynchronousPositioning = requiresAsynchronousPositioning;
            CreatedAtTicks = Stopwatch.GetTimestamp();
        }

        internal IntPtr WindowHandle { get; }
        internal int Left { get; }
        internal int Top { get; }
        internal bool RequiresAsynchronousPositioning { get; }
        internal long CreatedAtTicks { get; }

        internal bool HasSameTarget(WindowDragMotionRequest other)
        {
            return WindowHandle == other.WindowHandle && Left == other.Left && Top == other.Top &&
                   RequiresAsynchronousPositioning == other.RequiresAsynchronousPositioning;
        }
    }

    internal readonly struct WindowDragMotionResult
    {
        internal WindowDragMotionResult(long timestampMilliseconds, int requestedLeft,
            int requestedTop, bool succeeded, int errorCode, long callMicroseconds,
            bool usedAsynchronousFallback, long compositionWaitMicroseconds,
            long queueWaitMicroseconds = 0)
        {
            TimestampMilliseconds = timestampMilliseconds;
            RequestedLeft = requestedLeft;
            RequestedTop = requestedTop;
            Succeeded = succeeded;
            ErrorCode = errorCode;
            CallMicroseconds = callMicroseconds;
            UsedAsynchronousFallback = usedAsynchronousFallback;
            CompositionWaitMicroseconds = compositionWaitMicroseconds;
            QueueWaitMicroseconds = queueWaitMicroseconds;
        }

        internal long TimestampMilliseconds { get; }
        internal int RequestedLeft { get; }
        internal int RequestedTop { get; }
        internal bool Succeeded { get; }
        internal int ErrorCode { get; }
        internal long CallMicroseconds { get; }
        internal bool UsedAsynchronousFallback { get; }
        internal long CompositionWaitMicroseconds { get; }
        internal long QueueWaitMicroseconds { get; }

        internal WindowDragMotionResult WithWaitDurations(long compositionWaitMicroseconds,
            long queueWaitMicroseconds)
        {
            return new WindowDragMotionResult(TimestampMilliseconds, RequestedLeft,
                RequestedTop, Succeeded, ErrorCode, CallMicroseconds,
                UsedAsynchronousFallback, compositionWaitMicroseconds, queueWaitMicroseconds);
        }
    }

    internal interface IWindowDragMotionBackend
    {
        WindowDragMotionResult Apply(WindowDragMotionRequest request);
        long WaitForComposition();
    }

    internal sealed class NativeWindowDragMotionBackend : IWindowDragMotionBackend
    {
        private const int FallbackFrameMilliseconds = 8;

        public WindowDragMotionResult Apply(WindowDragMotionRequest request)
        {
            WindowPositionFlags flags = WindowPositionFlags.NoSize |
                                        WindowPositionFlags.NoZOrder |
                                        WindowPositionFlags.NoActivate |
                                        WindowPositionFlags.NoOwnerZOrder;
            bool useAsynchronousFallback = request.RequiresAsynchronousPositioning ||
                                           WindowPositionInterop.IsHungAppWindow(request.WindowHandle);
            if (useAsynchronousFallback)
                flags |= WindowPositionFlags.AsyncWindowPosition;

            long startedAt = Stopwatch.GetTimestamp();
            bool moved = WindowPositionInterop.SetWindowPosition(request.WindowHandle, IntPtr.Zero,
                request.Left, request.Top, 0, 0, flags);
            int errorCode = moved ? 0 : Marshal.GetLastWin32Error();

            if (!moved && !useAsynchronousFallback)
            {
                useAsynchronousFallback = true;
                flags |= WindowPositionFlags.AsyncWindowPosition;
                moved = WindowPositionInterop.SetWindowPosition(request.WindowHandle, IntPtr.Zero,
                    request.Left, request.Top, 0, 0, flags);
                errorCode = moved ? 0 : Marshal.GetLastWin32Error();
            }

            return new WindowDragMotionResult(Environment.TickCount64, request.Left, request.Top,
                moved, errorCode, GetElapsedMicroseconds(startedAt), useAsynchronousFallback, 0);
        }

        public long WaitForComposition()
        {
            long startedAt = Stopwatch.GetTimestamp();
            int result = WindowPositionInterop.FlushDesktopComposition();
            if (result < 0)
                Thread.Sleep(FallbackFrameMilliseconds);
            return GetElapsedMicroseconds(startedAt);
        }

        private static long GetElapsedMicroseconds(long startedAt)
        {
            long elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - startedAt);
            return (long)(elapsedTicks * (1_000_000d / Stopwatch.Frequency));
        }
    }

    internal sealed class WindowDragMotionPump : IDisposable
    {
        private readonly object _sync = new object();
        private readonly AutoResetEvent _workAvailable = new AutoResetEvent(false);
        private readonly ConcurrentQueue<WindowDragMotionResult> _results =
            new ConcurrentQueue<WindowDragMotionResult>();
        private readonly IWindowDragMotionBackend _backend;
        private readonly Thread _thread;

        private WindowDragMotionRequest _latestRequest;
        private long _latestVersion;
        private long _processingVersion;
        private long _appliedVersion;
        private int _coalescedTargets;
        private bool _stopping;
        private bool _stopped;
        private int _disposed;

        internal WindowDragMotionPump() : this(new NativeWindowDragMotionBackend())
        {
        }

        internal WindowDragMotionPump(IWindowDragMotionBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _thread = new Thread(ProcessMoves)
            {
                IsBackground = true,
                Name = "TouchPilot window drag motion pump",
                Priority = ThreadPriority.AboveNormal
            };
            _thread.Start();
        }

        internal int CoalescedTargets
        {
            get
            {
                lock (_sync)
                    return _coalescedTargets;
            }
        }

        internal bool Publish(WindowDragMotionRequest request)
        {
            lock (_sync)
            {
                if (_stopping || _stopped || Volatile.Read(ref _disposed) != 0)
                    return false;

                if (_latestVersion > _appliedVersion && _latestRequest.HasSameTarget(request))
                    return true;

                if (_latestVersion > _appliedVersion &&
                    _latestVersion != _processingVersion)
                {
                    _coalescedTargets++;
                }

                _latestRequest = request;
                _latestVersion++;
            }

            _workAvailable.Set();
            return true;
        }

        internal bool HasPendingMoves
        {
            get
            {
                lock (_sync)
                    return _latestVersion > _appliedVersion;
            }
        }

        internal bool Flush(TimeSpan timeout)
        {
            long targetVersion;
            lock (_sync)
            {
                targetVersion = _latestVersion;
                if (_appliedVersion >= targetVersion)
                    return true;
            }

            _workAvailable.Set();
            var stopwatch = Stopwatch.StartNew();
            lock (_sync)
            {
                while (_appliedVersion < targetVersion && !_stopped)
                {
                    TimeSpan remaining = timeout - stopwatch.Elapsed;
                    if (remaining <= TimeSpan.Zero || !Monitor.Wait(_sync, remaining))
                        break;
                }
                return _appliedVersion >= targetVersion;
            }
        }

        internal bool TryTakeResult(out WindowDragMotionResult result)
        {
            return _results.TryDequeue(out result);
        }

        internal bool Stop(TimeSpan timeout)
        {
            bool shouldWait;
            lock (_sync)
            {
                if (_stopped)
                    return true;

                shouldWait = !_stopping;
                _stopping = true;
                Monitor.PulseAll(_sync);
            }
            _workAvailable.Set();
            if (shouldWait && _thread.Join(timeout))
                return true;

            lock (_sync)
                return _stopped;
        }

        private void ProcessMoves()
        {
            try
            {
                while (true)
                {
                    _workAvailable.WaitOne();
                    while (TryTakeLatestRequest(out WindowDragMotionRequest request,
                               out long version))
                    {
                        long queueWait = (long)(Math.Max(0, Stopwatch.GetTimestamp() - request.CreatedAtTicks) *
                            (1_000_000d / Stopwatch.Frequency));
                        WindowDragMotionResult result = _backend.Apply(request);
                        long compositionWait = result.Succeeded
                            ? _backend.WaitForComposition()
                            : 0;
                        _results.Enqueue(result.WithWaitDurations(compositionWait, queueWait));

                        lock (_sync)
                        {
                            _appliedVersion = Math.Max(_appliedVersion, version);
                            _processingVersion = 0;
                            Monitor.PulseAll(_sync);
                        }
                    }

                    lock (_sync)
                    {
                        if (_stopping && _latestVersion <= _appliedVersion)
                            return;
                    }
                }
            }
            catch
            {
                // A failed optimized worker is observed as stopped by the controller,
                // which falls back to the established asynchronous SetWindowPos path.
            }
            finally
            {
                lock (_sync)
                {
                    _stopped = true;
                    Monitor.PulseAll(_sync);
                }
            }
        }

        private bool TryTakeLatestRequest(out WindowDragMotionRequest request, out long version)
        {
            lock (_sync)
            {
                if (_latestVersion <= _appliedVersion)
                {
                    request = default(WindowDragMotionRequest);
                    version = 0;
                    return false;
                }

                request = _latestRequest;
                version = _latestVersion;
                _processingVersion = version;
                return true;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            bool stopped = Stop(TimeSpan.FromMilliseconds(250));
            if (stopped)
                _workAvailable.Dispose();
        }
    }
}

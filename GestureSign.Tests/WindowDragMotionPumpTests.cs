using GestureSign.Daemon.Triggers;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace GestureSign.Tests
{
    public class WindowDragMotionPumpTests
    {
        [Fact]
        public void PumpCoalescesIntermediateTargetsWhileCompositionIsPending()
        {
            using var backend = new BlockingCompositionBackend();
            using var pump = new WindowDragMotionPump(backend);

            Assert.True(pump.Publish(Request(10)));
            Assert.True(backend.CompositionWaitStarted.Wait(TimeSpan.FromSeconds(2)));

            Assert.True(pump.Publish(Request(20)));
            Assert.True(pump.Publish(Request(30)));
            backend.AllowComposition.Set();

            Assert.True(pump.Flush(TimeSpan.FromSeconds(2)));
            Assert.True(pump.Stop(TimeSpan.FromSeconds(2)));

            var results = new List<WindowDragMotionResult>();
            while (pump.TryTakeResult(out WindowDragMotionResult result))
                results.Add(result);

            Assert.Equal(2, results.Count);
            Assert.Equal(10, results[0].RequestedLeft);
            Assert.Equal(30, results[1].RequestedLeft);
            Assert.Equal(1, pump.CoalescedTargets);
        }

        private static WindowDragMotionRequest Request(int left)
        {
            return new WindowDragMotionRequest(new IntPtr(123), left, left + 1, false);
        }

        private sealed class BlockingCompositionBackend : IWindowDragMotionBackend, IDisposable
        {
            internal ManualResetEventSlim CompositionWaitStarted { get; } =
                new ManualResetEventSlim(false);
            internal ManualResetEventSlim AllowComposition { get; } =
                new ManualResetEventSlim(false);

            public WindowDragMotionResult Apply(WindowDragMotionRequest request)
            {
                return new WindowDragMotionResult(Environment.TickCount64, request.Left,
                    request.Top, true, 0, 10, request.RequiresAsynchronousPositioning, 0);
            }

            public long WaitForComposition()
            {
                CompositionWaitStarted.Set();
                Assert.True(AllowComposition.Wait(TimeSpan.FromSeconds(2)));
                return 20;
            }

            public void Dispose()
            {
                AllowComposition.Set();
                CompositionWaitStarted.Dispose();
                AllowComposition.Dispose();
            }
        }
    }
}

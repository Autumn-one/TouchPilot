using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using Xunit;

namespace GestureSign.Tests
{
    public class GestureManagerTests
    {
        [Fact]
        public void RepeatedCapturesAreRecognizedIndependently()
        {
            var manager = CreateEmptyManager();
            var tap = CreateThreeFingerTap();

            manager.AddGesture(new Gesture("single", new[] { tap }));
            manager.AddGesture(new Gesture("multi", new[] { tap, tap }));

            Assert.Single(manager.Gestures);

            var capture = new FakePointCapture();
            manager.Load(capture);

            Assert.Equal(0, capture.CaptureStartedSubscriptionCount);

            capture.RaiseBeforePointsCaptured(tap.Points);
            Assert.Equal("single", manager.GestureName);

            capture.RaiseBeforePointsCaptured(tap.Points);
            Assert.Equal("single", manager.GestureName);
        }

        [Fact]
        public void LoadingGestureFileDropsMultiSegmentEntries()
        {
            string filePath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(filePath,
                    "[{\"Name\":\"single\",\"PointPatterns\":[{\"Points\":[[\"0, 0\"],[\"1, 1\"],[\"2, 2\"]]}]}," +
                    "{\"Name\":\"multi\",\"PointPatterns\":[{\"Points\":[[\"0, 0\"],[\"1, 1\"],[\"2, 2\"]]}," +
                    "{\"Points\":[[\"0, 0\"],[\"1, 1\"],[\"2, 2\"]]}]}]");

                List<IGesture> gestures = GestureManager.LoadGesturesFromFile(filePath, true);

                IGesture gesture = Assert.Single(gestures);
                Assert.Equal("single", gesture.Name);
                Assert.Single(gesture.PointPatterns);
            }
            finally
            {
                File.Delete(filePath);
            }
        }

        private static TestGestureManager CreateEmptyManager()
        {
            var manager = new TestGestureManager();
            manager.LoadingTask.GetAwaiter().GetResult();

            foreach (string name in manager.Gestures
                         .Select(gesture => gesture.Name)
                         .Where(name => !string.IsNullOrWhiteSpace(name))
                         .Distinct()
                         .ToArray())
            {
                manager.DeleteGesture(name);
            }

            return manager;
        }

        private static PointPattern CreateThreeFingerTap()
        {
            return new PointPattern(new[]
            {
                new[] { new Point(10, 10) },
                new[] { new Point(20, 20) },
                new[] { new Point(30, 30) }
            });
        }

        private sealed class TestGestureManager : GestureManager
        {
        }

        private sealed class FakePointCapture : IPointCapture
        {
            private PointsCapturedEventHandler _captureStarted;

            public int CaptureStartedSubscriptionCount => _captureStarted?.GetInvocationList().Length ?? 0;

            public event PointsCapturedEventHandler AfterPointsCaptured
            {
                add { }
                remove { }
            }
            public event PointsCapturedEventHandler BeforePointsCaptured;
            public event PointsCapturedEventHandler CaptureStarted
            {
                add => _captureStarted += value;
                remove => _captureStarted -= value;
            }
            public event EventHandler CaptureEnded
            {
                add { }
                remove { }
            }
            public event RecognitionEventHandler GestureRecognized
            {
                add { }
                remove { }
            }
            public event PointsCapturedEventHandler PointCaptured
            {
                add { }
                remove { }
            }

            public bool TemporarilyDisableCapture { get; set; }
            public Devices SourceDevice => Devices.TouchPad;
            public CaptureState State { get; set; }
            public CaptureMode Mode { get; set; } = CaptureMode.Normal;

            public void RaiseBeforePointsCaptured(Point[][] points)
            {
                var capturedPoints = points.Select(stroke => stroke.ToList()).ToList();
                BeforePointsCaptured?.Invoke(this, new PointsCapturedEventArgs(capturedPoints, new List<Point>()));
            }
        }
    }
}

using GestureSign.Daemon.Triggers;
using System;
using System.Drawing;
using Xunit;

namespace GestureSign.Tests
{
    public class WindowDragTrajectoryTests
    {
        private static readonly Rectangle Desktop = new Rectangle(-1920, -1080, 5760, 3240);

        [Fact]
        public void StationarySubpixelNoiseDoesNotOscillateAcrossPixelBoundary()
        {
            var trajectory = Create();
            for (int i = 1; i <= 240; i++)
            {
                double noise = i % 2 == 0 ? 0.6 : -0.6;
                Point cursor = trajectory.Update(0.5 + noise / 1920, 0.5 - noise / 1080,
                    1, i / 120d, Desktop);
                Assert.Equal(new Point(100, 100), cursor);
            }
        }

        [Fact]
        public void SlowSubpixelMovementAccumulatesWithoutADeadZone()
        {
            var trajectory = Create();
            Point cursor = default;
            for (int i = 1; i <= 400; i++)
                cursor = trajectory.Update(0.5 + i * 0.05 / 1920, 0.5,
                    1, i / 120d, Desktop);
            Assert.InRange(cursor.X, 118, 120);
            Assert.Equal(100, cursor.Y);
        }

        [Fact]
        public void FastMotionAndReversalStayWithinTwoPixelsWithoutOvershoot()
        {
            var trajectory = Create();
            int previous = 100;
            for (int i = 1; i <= 200; i++)
            {
                double offset = i <= 100 ? i * 8 : (200 - i) * 8;
                Point cursor = trajectory.Update(0.5 + offset / 1920, 0.5,
                    1, i / 144d, Desktop);
                Assert.InRange(Math.Abs(cursor.X - (100 + offset)), 0, 2);
                Assert.True(i <= 100 ? cursor.X >= previous : cursor.X <= previous);
                previous = cursor.X;
            }
        }

        [Fact]
        public void CrossingDisplaysKeepsTheInitialPixelScale()
        {
            var trajectory = Create(new Point(1910, 100));
            trajectory.Update(0.51, 0.5, 1, 0.01, Desktop);
            Point cursor = trajectory.Update(0.52, 0.5, 1, 0.02, Desktop);
            Assert.InRange(cursor.X, 1947, 1949);
        }

        [Fact]
        public void RebaseClearsFilterHistoryAndRetainsTheMovementScale()
        {
            var trajectory = Create();
            trajectory.Update(0.8, 0.8, 1, 0.01, Desktop);
            trajectory.Rebase(0.2, 0.2, new Point(-300, 200), 1);
            Assert.Equal(new Point(-300, 200), trajectory.Update(0.2, 0.2, 1, 1.01, Desktop));
            Point cursor = trajectory.Update(0.21, 0.2, 1, 1.02, Desktop);
            Assert.InRange(cursor.X, -282, -280);
            Assert.Equal(200, cursor.Y);
        }

        [Fact]
        public void DesktopClampDoesNotAccumulateUnreachableMotion()
        {
            var trajectory = Create(new Point(3830, 100));
            trajectory.Update(0.9, 0.5, 1, 0.01, Desktop);
            Point cursor = trajectory.Update(0.89, 0.5, 1, 0.02, Desktop);
            Assert.InRange(cursor.X, 3819, 3821);
        }

        [Fact]
        public void InvalidSamplesDoNotCorruptTheNextPosition()
        {
            var trajectory = Create();
            Assert.Equal(new Point(100, 100), trajectory.Update(double.NaN, 0.5, 1, 0.01, Desktop));
            Point cursor = trajectory.Update(0.51, 0.5, 1, 0.02, Desktop);
            Assert.InRange(cursor.X, 118, 120);
        }

        private static WindowDragTrajectory Create(Point? cursor = null)
        {
            var trajectory = new WindowDragTrajectory();
            trajectory.Begin(0.5, 0.5, cursor ?? new Point(100, 100), new Size(1920, 1080), 0);
            return trajectory;
        }
    }
}

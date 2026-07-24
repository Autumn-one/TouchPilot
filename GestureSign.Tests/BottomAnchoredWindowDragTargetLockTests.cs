using GestureSign.Common.Input;
using GestureSign.Daemon.Triggers;
using ManagedWinapi.Windows;
using System;
using System.Drawing;
using Xunit;

namespace GestureSign.Tests
{
    public class BottomAnchoredWindowDragTargetLockTests
    {
        [Fact]
        public void CandidateStartLocksResolvedWindowAtFrameCursorUntilTaken()
        {
            var targetLock = new BottomAnchoredWindowDragTargetLock();
            var frameCursor = new Point(420, 240);
            var expectedWindow = new SystemWindow(new IntPtr(123));

            targetLock.Update(Frame(candidateStarted: true), frameCursor, point =>
            {
                Assert.Equal(frameCursor, point);
                return expectedWindow;
            });

            Assert.True(targetLock.TryTake(out SystemWindow window, out Point cursorPosition));
            Assert.Same(expectedWindow, window);
            Assert.Equal(frameCursor, cursorPosition);
            Assert.False(targetLock.TryTake(out _, out _));
        }

        [Fact]
        public void CandidateReplacementClearsThenLocksTheNewFrameTarget()
        {
            var targetLock = new BottomAnchoredWindowDragTargetLock();
            var firstWindow = new SystemWindow(new IntPtr(123));
            var replacementWindow = new SystemWindow(new IntPtr(456));

            targetLock.Update(Frame(candidateStarted: true), new Point(100, 100), _ => firstWindow);
            targetLock.Update(Frame(candidateStarted: true, candidateEnded: true),
                new Point(200, 200), _ => replacementWindow);

            Assert.True(targetLock.TryTake(out SystemWindow window, out Point cursorPosition));
            Assert.Same(replacementWindow, window);
            Assert.Equal(new Point(200, 200), cursorPosition);
        }

        [Fact]
        public void MissingCapturedWindowRemainsLockedAndDoesNotBecomeFallbackEligible()
        {
            var targetLock = new BottomAnchoredWindowDragTargetLock();
            var frameCursor = new Point(320, 180);

            targetLock.Update(Frame(candidateStarted: true), frameCursor, _ => null);

            Assert.True(targetLock.TryTake(out SystemWindow window, out Point cursorPosition));
            Assert.Null(window);
            Assert.Equal(frameCursor, cursorPosition);
        }

        [Fact]
        public void CandidateEndClearsPendingTarget()
        {
            var targetLock = new BottomAnchoredWindowDragTargetLock();
            targetLock.Update(Frame(candidateStarted: true), new Point(100, 100),
                _ => new SystemWindow(new IntPtr(123)));

            targetLock.Update(Frame(candidateEnded: true), new Point(200, 200),
                _ => throw new InvalidOperationException("An ended candidate must not resolve a new window."));

            Assert.False(targetLock.TryTake(out _, out _));
        }

        private static TouchpadInteractionFrameResult Frame(bool candidateStarted = false,
            bool candidateEnded = false)
        {
            return new TouchpadInteractionFrameResult(false, true,
                Array.Empty<TouchpadInteractionEvent>(), candidateStarted, candidateEnded);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using GestureSign.Common.Input;
using Xunit;

namespace GestureSign.Tests
{
    public class TouchpadInteractionRecognizerTests
    {
        [Fact]
        public void DisabledRecognizerDoesNotClaimEdgeMovement()
        {
            var recognizer = CreateRecognizer();

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.5)), 0);
            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(Frame(Contact(1, 0.15, 0.5)), 100);

            Assert.False(result.ClaimInput);
            Assert.Empty(result.Events);
        }

        [Fact]
        public void LeftInwardSwipeClaimsAndFiresOnce()
        {
            var recognizer = CreateRecognizer(FixedEdgeGesture.LeftSwipeIn);

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.5)), 0);
            TouchpadInteractionFrameResult fired = recognizer.ProcessFrame(Frame(Contact(1, 0.12, 0.51)), 100);
            TouchpadInteractionFrameResult continued = recognizer.ProcessFrame(Frame(Contact(1, 0.25, 0.51)), 150);

            Assert.True(fired.ClaimInput);
            Assert.Equal(FixedEdgeGesture.LeftSwipeIn, Assert.Single(fired.Events).EdgeGesture);
            Assert.True(continued.ClaimInput);
            Assert.Empty(continued.Events);
        }

        [Fact]
        public void EdgeSlideRepeatsAndCanReverseDirection()
        {
            var recognizer = CreateRecognizer(FixedEdgeGesture.LeftSlideUp, FixedEdgeGesture.LeftSlideDown);

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.55)), 0);
            TouchpadInteractionFrameResult upward = recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.43)), 100);
            TouchpadInteractionFrameResult downward = recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.60)), 180);

            Assert.True(upward.ClaimInput);
            Assert.All(upward.Events, item => Assert.Equal(FixedEdgeGesture.LeftSlideUp, item.EdgeGesture));
            Assert.Contains(downward.Events, item => item.EdgeGesture == FixedEdgeGesture.LeftSlideDown);
        }

        [Fact]
        public void ExtraContactPreventsOrdinaryEdgeSwipeClaim()
        {
            var recognizer = CreateRecognizer(FixedEdgeGesture.LeftSwipeIn);

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.5)), 0);
            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(
                Frame(Contact(1, 0.14, 0.5), Contact(2, 0.6, 0.6)), 100);

            Assert.False(result.ClaimInput);
            Assert.Empty(result.Events);
        }

        [Fact]
        public void BottomAnchorRequiresHoldAndMovingFingerMotion()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);

            recognizer.ProcessFrame(Frame(Contact(0, 0.5, 0.96)), 0);
            TouchpadInteractionFrameResult beforeHold = recognizer.ProcessFrame(
                Frame(Contact(0, 0.5, 0.96), Contact(1, 0.5, 0.5)), 50);
            TouchpadInteractionFrameResult stationary = recognizer.ProcessFrame(
                Frame(Contact(0, 0.5, 0.96), Contact(1, 0.5, 0.5)), 120);
            TouchpadInteractionFrameResult started = recognizer.ProcessFrame(
                Frame(Contact(0, 0.5, 0.96), Contact(1, 0.53, 0.5)), 140);

            Assert.False(beforeHold.ClaimInput);
            Assert.False(stationary.ClaimInput);
            Assert.True(started.ClaimInput);
            Assert.Equal(TouchpadInteractionEventType.WindowDragStarted, Assert.Single(started.Events).EventType);
        }

        [Fact]
        public void AnchorDriftCancelsCandidateWithoutClaiming()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);

            recognizer.ProcessFrame(Frame(Contact(3, 0.5, 0.96)), 0);
            recognizer.ProcessFrame(Frame(Contact(3, 0.54, 0.96), Contact(4, 0.5, 0.5)), 120);
            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(
                Frame(Contact(3, 0.54, 0.96), Contact(4, 0.55, 0.5)), 150);

            Assert.False(result.ClaimInput);
            Assert.Empty(result.Events);
        }

        [Fact]
        public void ClaimedDragSupportsPauseReclutchAndAnchorRelease()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);

            recognizer.ProcessFrame(Frame(Contact(10, 0.5, 0.96)), 0);
            recognizer.ProcessFrame(Frame(Contact(10, 0.5, 0.96), Contact(11, 0.5, 0.5)), 110);
            recognizer.ProcessFrame(Frame(Contact(10, 0.5, 0.96), Contact(11, 0.53, 0.5)), 130);
            TouchpadInteractionFrameResult paused = recognizer.ProcessFrame(Frame(Contact(10, 0.5, 0.96)), 160);
            TouchpadInteractionFrameResult resumed = recognizer.ProcessFrame(
                Frame(Contact(10, 0.5, 0.96), Contact(12, 0.4, 0.4)), 180);
            TouchpadInteractionFrameResult ended = recognizer.ProcessFrame(Frame(Contact(12, 0.4, 0.4)), 200);
            TouchpadInteractionFrameResult released = recognizer.ProcessFrame(Frame(), 220);

            Assert.Equal(TouchpadInteractionEventType.WindowDragPaused, Assert.Single(paused.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragResumed, Assert.Single(resumed.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragEnded, Assert.Single(ended.Events).EventType);
            Assert.True(ended.ClaimInput);
            Assert.True(released.ClaimInput);
            Assert.False(released.SessionActive);
        }

        [Fact]
        public void FreeAnchorRequiresFirstFingerToArriveAlone()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.FreeTwoFingerAnchor);

            recognizer.ProcessFrame(Frame(Contact(1, 0.4, 0.4)), 0);
            recognizer.ProcessFrame(Frame(Contact(1, 0.4, 0.4), Contact(2, 0.6, 0.6)), 190);
            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(
                Frame(Contact(1, 0.4, 0.4), Contact(2, 0.63, 0.6)), 220);

            Assert.True(result.ClaimInput);
            Assert.Equal(TouchpadInteractionEventType.WindowDragStarted, Assert.Single(result.Events).EventType);
        }

        private static TouchpadInteractionRecognizer CreateRecognizer(
            FixedEdgeGesture gesture1 = FixedEdgeGesture.None,
            FixedEdgeGesture gesture2 = FixedEdgeGesture.None,
            TouchpadWindowDragMode windowDragMode = TouchpadWindowDragMode.Disabled)
        {
            var enabled = new HashSet<FixedEdgeGesture>(new[] { gesture1, gesture2 }.Where(gesture => gesture != FixedEdgeGesture.None));
            return new TouchpadInteractionRecognizer(new TouchpadInteractionOptions
            {
                EdgeGesturesEnabled = enabled.Count != 0,
                EnabledEdgeGestures = enabled,
                WindowDragMode = windowDragMode
            });
        }

        private static TouchpadContact Contact(int id, double x, double y)
        {
            return new TouchpadContact(id, DeviceStates.Tip, x, y);
        }

        private static IReadOnlyList<TouchpadContact> Frame(params TouchpadContact[] contacts)
        {
            return contacts;
        }
    }
}

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
        public void TwoFingerInwardSwipeRequiresBothContactsFromSameEdge()
        {
            var recognizer = CreateRecognizer(FixedEdgeGesture.TwoFingerLeftSwipeIn);

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.4)), 0);
            TouchpadInteractionFrameResult joined = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.4), Contact(2, 0.03, 0.65)), 20);
            TouchpadInteractionFrameResult fired = recognizer.ProcessFrame(
                Frame(Contact(1, 0.13, 0.4), Contact(2, 0.14, 0.65)), 100);

            Assert.False(joined.ClaimInput);
            Assert.Empty(joined.Events);
            Assert.True(fired.ClaimInput);
            Assert.Equal(FixedEdgeGesture.TwoFingerLeftSwipeIn, Assert.Single(fired.Events).EdgeGesture);
        }

        [Fact]
        public void TwoFingerEdgeGestureDoesNotFireWhenOnlyOneFingerMoves()
        {
            var recognizer = CreateRecognizer(FixedEdgeGesture.TwoFingerLeftSwipeIn);

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.4)), 0);
            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.4), Contact(2, 0.03, 0.65)), 20);
            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(
                Frame(Contact(1, 0.14, 0.4), Contact(2, 0.03, 0.65)), 100);

            Assert.False(result.ClaimInput);
            Assert.Empty(result.Events);
        }

        [Fact]
        public void TwoFingerEdgeGestureDoesNotCombineDifferentEdges()
        {
            var recognizer = CreateRecognizer(FixedEdgeGesture.TwoFingerLeftSwipeIn);

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.4)), 0);
            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.4), Contact(2, 0.98, 0.6)), 20);
            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(
                Frame(Contact(1, 0.14, 0.4), Contact(2, 0.86, 0.6)), 100);

            Assert.False(result.ClaimInput);
            Assert.Empty(result.Events);
        }

        [Fact]
        public void TwoFingerEdgeSlideRepeatsAndCanReverseDirection()
        {
            var recognizer = CreateRecognizer(
                FixedEdgeGesture.TwoFingerLeftSlideUp,
                FixedEdgeGesture.TwoFingerLeftSlideDown);

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.55)), 0);
            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.55), Contact(2, 0.03, 0.65)), 20);
            TouchpadInteractionFrameResult upward = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.43), Contact(2, 0.03, 0.53)), 100);
            TouchpadInteractionFrameResult oneFingerContinued = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.30), Contact(2, 0.03, 0.53)), 140);
            TouchpadInteractionFrameResult downward = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.62), Contact(2, 0.03, 0.72)), 180);

            Assert.True(upward.ClaimInput);
            Assert.All(upward.Events, item => Assert.Equal(FixedEdgeGesture.TwoFingerLeftSlideUp, item.EdgeGesture));
            Assert.Empty(oneFingerContinued.Events);
            Assert.Contains(downward.Events, item => item.EdgeGesture == FixedEdgeGesture.TwoFingerLeftSlideDown);
        }

        [Fact]
        public void InteriorSecondContactPreservesBottomAnchoredWindowDrag()
        {
            var recognizer = CreateRecognizer(
                FixedEdgeGesture.TwoFingerBottomSlideRight,
                windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);

            recognizer.ProcessFrame(Frame(Contact(0, 0.4, 0.96)), 0);
            recognizer.ProcessFrame(Frame(Contact(0, 0.4, 0.96), Contact(1, 0.5, 0.5)), 50);
            recognizer.ProcessFrame(Frame(Contact(0, 0.4, 0.96), Contact(1, 0.5, 0.5)), 120);
            TouchpadInteractionFrameResult started = recognizer.ProcessFrame(
                Frame(Contact(0, 0.4, 0.96), Contact(1, 0.54, 0.5)), 140);

            Assert.True(started.ClaimInput);
            Assert.Equal(TouchpadInteractionEventType.WindowDragStarted, Assert.Single(started.Events).EventType);
        }

        [Fact]
        public void CoherentTwoFingerBottomSlideWinsOverWindowDrag()
        {
            var recognizer = CreateRecognizer(
                FixedEdgeGesture.TwoFingerBottomSlideRight,
                windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);

            recognizer.ProcessFrame(Frame(Contact(0, 0.4, 0.96)), 0);
            recognizer.ProcessFrame(Frame(Contact(0, 0.4, 0.96), Contact(1, 0.6, 0.95)), 20);
            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(
                Frame(Contact(0, 0.48, 0.96), Contact(1, 0.68, 0.95)), 120);

            Assert.True(result.ClaimInput);
            Assert.NotEmpty(result.Events);
            Assert.All(result.Events, item =>
            {
                Assert.Equal(TouchpadInteractionEventType.EdgeGesture, item.EventType);
                Assert.Equal(FixedEdgeGesture.TwoFingerBottomSlideRight, item.EdgeGesture);
            });
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
        public void BottomAnchorCanMoveWithinEdgeZoneBeforeActivation()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);

            recognizer.ProcessFrame(Frame(Contact(3, 0.2, 0.96)), 0);
            TouchpadInteractionFrameResult beforeHold = recognizer.ProcessFrame(
                Frame(Contact(3, 0.65, 0.91), Contact(4, 0.5, 0.5)), 50);
            TouchpadInteractionFrameResult started = recognizer.ProcessFrame(
                Frame(Contact(3, 0.8, 0.89), Contact(4, 0.54, 0.48)), 120);

            Assert.False(beforeHold.ClaimInput);
            Assert.True(started.ClaimInput);
            Assert.Equal(TouchpadInteractionEventType.WindowDragStarted, Assert.Single(started.Events).EventType);
        }

        [Fact]
        public void ActiveDragContinuesWhenAnchorMovesWithinBottomZone()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);
            StartBottomAnchoredDrag(recognizer, 10, 11);

            TouchpadInteractionFrameResult continued = recognizer.ProcessFrame(
                Frame(Contact(10, 0.82, 0.90), Contact(11, 0.56, 0.53)), 160);

            Assert.True(continued.ClaimInput);
            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(continued.Events).EventType);
        }

        [Fact]
        public void AnchorLeavingBottomZoneEndsDragImmediately()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);
            StartBottomAnchoredDrag(recognizer, 10, 11);

            TouchpadInteractionFrameResult ended = recognizer.ProcessFrame(
                Frame(Contact(10, 0.7, 0.80), Contact(11, 0.55, 0.5)), 140);

            Assert.True(ended.ClaimInput);
            Assert.Equal(TouchpadInteractionEventType.WindowDragEnded, Assert.Single(ended.Events).EventType);
        }

        [Fact]
        public void BriefMissingAnchorReportKeepsDragActive()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);
            StartBottomAnchoredDrag(recognizer, 10, 11);

            TouchpadInteractionFrameResult missing = recognizer.ProcessFrame(Frame(Contact(11, 0.55, 0.5)), 150);
            TouchpadInteractionFrameResult returned = recognizer.ProcessFrame(
                Frame(Contact(10, 0.75, 0.91), Contact(11, 0.57, 0.51)), 210);

            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(missing.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(returned.Events).EventType);
            Assert.True(returned.ClaimInput);
        }

        [Fact]
        public void BottomContactWithNewIdentifierContinuesAnchor()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);
            StartBottomAnchoredDrag(recognizer, 10, 11);

            TouchpadInteractionFrameResult replaced = recognizer.ProcessFrame(
                Frame(Contact(12, 0.6, 0.94), Contact(11, 0.55, 0.5)), 150);
            TouchpadInteractionFrameResult continued = recognizer.ProcessFrame(
                Frame(Contact(12, 0.82, 0.90), Contact(11, 0.57, 0.51)), 180);

            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(replaced.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(continued.Events).EventType);
            Assert.True(continued.ClaimInput);
        }

        [Fact]
        public void MovingFingerCannotReplaceMissingAnchor()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);
            StartBottomAnchoredDrag(recognizer, 10, 11);

            TouchpadInteractionFrameResult missing = recognizer.ProcessFrame(Frame(Contact(11, 0.55, 0.95)), 150);
            TouchpadInteractionFrameResult expired = recognizer.ProcessFrame(Frame(Contact(11, 0.57, 0.94)), 231);

            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(missing.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragEnded, Assert.Single(expired.Events).EventType);
        }

        [Fact]
        public void ClaimedDragSupportsMovingFingerPauseAndReclutch()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);
            StartBottomAnchoredDrag(recognizer, 10, 11);

            TouchpadInteractionFrameResult paused = recognizer.ProcessFrame(Frame(Contact(10, 0.5, 0.96)), 160);
            TouchpadInteractionFrameResult resumed = recognizer.ProcessFrame(
                Frame(Contact(10, 0.7, 0.92), Contact(12, 0.4, 0.4)), 180);
            TouchpadInteractionFrameResult released = recognizer.ProcessFrame(Frame(), 220);

            Assert.Equal(TouchpadInteractionEventType.WindowDragPaused, Assert.Single(paused.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragResumed, Assert.Single(resumed.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragEnded, Assert.Single(released.Events).EventType);
            Assert.True(released.ClaimInput);
            Assert.False(released.SessionActive);
        }

        private static void StartBottomAnchoredDrag(TouchpadInteractionRecognizer recognizer, int anchorIdentifier, int movingIdentifier)
        {
            recognizer.ProcessFrame(Frame(Contact(anchorIdentifier, 0.5, 0.96)), 0);
            recognizer.ProcessFrame(
                Frame(Contact(anchorIdentifier, 0.5, 0.96), Contact(movingIdentifier, 0.5, 0.5)), 110);
            TouchpadInteractionFrameResult started = recognizer.ProcessFrame(
                Frame(Contact(anchorIdentifier, 0.5, 0.96), Contact(movingIdentifier, 0.53, 0.5)), 130);

            Assert.Equal(TouchpadInteractionEventType.WindowDragStarted, Assert.Single(started.Events).EventType);
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

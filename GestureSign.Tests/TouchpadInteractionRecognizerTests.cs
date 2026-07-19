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
        public void ThreeFingerInwardSwipeRequiresAllContactsFromSameEdge()
        {
            var recognizer = CreateRecognizer(FixedEdgeGesture.ThreeFingerLeftSwipeIn);

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.3)), 0);
            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.3), Contact(2, 0.03, 0.5)), 20);
            TouchpadInteractionFrameResult joined = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.3), Contact(2, 0.03, 0.5), Contact(3, 0.02, 0.7)), 40);
            TouchpadInteractionFrameResult fired = recognizer.ProcessFrame(
                Frame(Contact(1, 0.13, 0.3), Contact(2, 0.14, 0.5), Contact(3, 0.13, 0.7)), 100);

            Assert.False(joined.ClaimInput);
            Assert.Empty(joined.Events);
            Assert.True(fired.ClaimInput);
            Assert.Equal(FixedEdgeGesture.ThreeFingerLeftSwipeIn, Assert.Single(fired.Events).EdgeGesture);
        }

        [Fact]
        public void ThreeFingerEdgeGestureDoesNotFireWhenOnlyTwoFingersMove()
        {
            var recognizer = CreateRecognizer(FixedEdgeGesture.ThreeFingerLeftSwipeIn);

            recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.3), Contact(2, 0.03, 0.5), Contact(3, 0.02, 0.7)), 0);
            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(
                Frame(Contact(1, 0.14, 0.3), Contact(2, 0.15, 0.5), Contact(3, 0.02, 0.7)), 100);

            Assert.False(result.ClaimInput);
            Assert.Empty(result.Events);
        }

        [Fact]
        public void ThreeFingerEdgeGestureDoesNotCombineDifferentEdges()
        {
            var recognizer = CreateRecognizer(FixedEdgeGesture.ThreeFingerLeftSwipeIn);

            TouchpadInteractionFrameResult pressed = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.3), Contact(2, 0.03, 0.5), Contact(3, 0.98, 0.7)), 0);
            TouchpadInteractionFrameResult moved = recognizer.ProcessFrame(
                Frame(Contact(1, 0.14, 0.3), Contact(2, 0.15, 0.5), Contact(3, 0.86, 0.7)), 100);

            Assert.False(pressed.ClaimInput);
            Assert.False(moved.ClaimInput);
            Assert.Empty(moved.Events);
        }

        [Fact]
        public void ThreeFingerEdgeSlideRepeatsAndCanReverseDirection()
        {
            var recognizer = CreateRecognizer(
                FixedEdgeGesture.ThreeFingerLeftSlideUp,
                FixedEdgeGesture.ThreeFingerLeftSlideDown);

            recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.4), Contact(2, 0.03, 0.55), Contact(3, 0.02, 0.7)), 0);
            TouchpadInteractionFrameResult upward = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.28), Contact(2, 0.03, 0.43), Contact(3, 0.02, 0.58)), 100);
            TouchpadInteractionFrameResult inconsistent = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.18), Contact(2, 0.03, 0.43), Contact(3, 0.02, 0.58)), 140);
            TouchpadInteractionFrameResult downward = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.48), Contact(2, 0.03, 0.63), Contact(3, 0.02, 0.78)), 180);

            Assert.True(upward.ClaimInput);
            Assert.All(upward.Events, item => Assert.Equal(FixedEdgeGesture.ThreeFingerLeftSlideUp, item.EdgeGesture));
            Assert.Empty(inconsistent.Events);
            Assert.Contains(downward.Events, item => item.EdgeGesture == FixedEdgeGesture.ThreeFingerLeftSlideDown);
        }

        [Fact]
        public void ThreeFingerBottomSlideRightUsesThreeFingerBinding()
        {
            var recognizer = CreateRecognizer(FixedEdgeGesture.ThreeFingerBottomSlideRight);

            recognizer.ProcessFrame(
                Frame(Contact(1, 0.25, 0.98), Contact(2, 0.5, 0.97), Contact(3, 0.75, 0.98)), 0);
            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(
                Frame(Contact(1, 0.33, 0.98), Contact(2, 0.58, 0.97), Contact(3, 0.83, 0.98)), 100);

            Assert.True(result.ClaimInput);
            Assert.NotEmpty(result.Events);
            Assert.All(result.Events,
                item => Assert.Equal(FixedEdgeGesture.ThreeFingerBottomSlideRight, item.EdgeGesture));
        }

        [Fact]
        public void ThreeFingerEdgeGestureWinsOverThreeFingerDragAtTheEdge()
        {
            var recognizer = CreateRecognizer(
                FixedEdgeGesture.ThreeFingerLeftSwipeIn,
                windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);

            TouchpadInteractionFrameResult pressed = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.3), Contact(2, 0.03, 0.5), Contact(3, 0.02, 0.7)), 0);
            TouchpadInteractionFrameResult fired = recognizer.ProcessFrame(
                Frame(Contact(1, 0.13, 0.3), Contact(2, 0.14, 0.5), Contact(3, 0.13, 0.7)), 100);

            Assert.True(pressed.ClaimInput);
            Assert.Empty(pressed.Events);
            Assert.True(fired.ClaimInput);
            Assert.Equal(FixedEdgeGesture.ThreeFingerLeftSwipeIn, Assert.Single(fired.Events).EdgeGesture);
        }

        [Fact]
        public void InteriorThreeFingerDragStillWorksWhenEdgeGestureIsAssigned()
        {
            var recognizer = CreateRecognizer(
                FixedEdgeGesture.ThreeFingerLeftSwipeIn,
                windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);

            recognizer.ProcessFrame(
                Frame(Contact(1, 0.2, 0.3), Contact(2, 0.5, 0.3), Contact(3, 0.8, 0.3)), 0);
            TouchpadInteractionFrameResult started = recognizer.ProcessFrame(
                Frame(Contact(1, 0.23, 0.3), Contact(2, 0.53, 0.3), Contact(3, 0.83, 0.3)), 40);

            Assert.True(started.ClaimInput);
            Assert.Equal(TouchpadInteractionEventType.WindowDragStarted, Assert.Single(started.Events).EventType);
        }

        [Fact]
        public void ExpiredThreeFingerEdgeCandidateRebasesBeforeFallingBackToDrag()
        {
            var recognizer = CreateRecognizer(
                FixedEdgeGesture.ThreeFingerLeftSwipeIn,
                windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);

            TouchpadInteractionFrameResult pressed = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.3), Contact(2, 0.03, 0.5), Contact(3, 0.02, 0.7)), 0);
            TouchpadInteractionFrameResult expired = recognizer.ProcessFrame(
                Frame(Contact(1, 0.02, 0.3), Contact(2, 0.03, 0.5), Contact(3, 0.02, 0.7)), 900);
            TouchpadInteractionFrameResult started = recognizer.ProcessFrame(
                Frame(Contact(1, 0.05, 0.3), Contact(2, 0.06, 0.5), Contact(3, 0.05, 0.7)), 930);

            Assert.True(pressed.ClaimInput);
            Assert.Empty(pressed.Events);
            Assert.True(expired.ClaimInput);
            Assert.Empty(expired.Events);
            Assert.Equal(TouchpadInteractionEventType.WindowDragStarted, Assert.Single(started.Events).EventType);
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
        public void MovingFingerCanArriveBeforeBottomAnchor()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);

            recognizer.ProcessFrame(Frame(Contact(1, 0.45, 0.45)), 0);
            TouchpadInteractionFrameResult anchorArrived = recognizer.ProcessFrame(
                Frame(Contact(1, 0.48, 0.45), Contact(2, 0.25, 0.96)), 30);
            TouchpadInteractionFrameResult beforeHold = recognizer.ProcessFrame(
                Frame(Contact(1, 0.50, 0.46), Contact(2, 0.55, 0.92)), 100);
            TouchpadInteractionFrameResult started = recognizer.ProcessFrame(
                Frame(Contact(1, 0.53, 0.48), Contact(2, 0.75, 0.90)), 140);

            Assert.False(anchorArrived.ClaimInput);
            Assert.False(beforeHold.ClaimInput);
            Assert.True(started.ClaimInput);
            Assert.Equal(TouchpadInteractionEventType.WindowDragStarted, Assert.Single(started.Events).EventType);
        }

        [Fact]
        public void ClaimedEdgeGestureCannotBeReplacedByLateBottomAnchor()
        {
            var recognizer = CreateRecognizer(
                FixedEdgeGesture.LeftSwipeIn,
                windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.5)), 0);
            TouchpadInteractionFrameResult edgeFired = recognizer.ProcessFrame(Frame(Contact(1, 0.14, 0.5)), 100);
            TouchpadInteractionFrameResult anchorArrived = recognizer.ProcessFrame(
                Frame(Contact(1, 0.17, 0.5), Contact(2, 0.5, 0.96)), 130);
            TouchpadInteractionFrameResult continued = recognizer.ProcessFrame(
                Frame(Contact(1, 0.22, 0.5), Contact(2, 0.7, 0.92)), 260);

            Assert.Equal(FixedEdgeGesture.LeftSwipeIn, Assert.Single(edgeFired.Events).EdgeGesture);
            Assert.True(anchorArrived.ClaimInput);
            Assert.Empty(anchorArrived.Events);
            Assert.True(continued.ClaimInput);
            Assert.Empty(continued.Events);
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
        public void ReleasedAnchorPausesAndCanReclutchWhileMovingFingerStaysDown()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);
            StartBottomAnchoredDrag(recognizer, 10, 11);

            TouchpadInteractionFrameResult missing = recognizer.ProcessFrame(Frame(Contact(11, 0.55, 0.95)), 150);
            TouchpadInteractionFrameResult paused = recognizer.ProcessFrame(Frame(Contact(11, 0.57, 0.94)), 231);
            TouchpadInteractionFrameResult stillPaused = recognizer.ProcessFrame(Frame(Contact(11, 0.59, 0.92)), 400);
            TouchpadInteractionFrameResult resumed = recognizer.ProcessFrame(
                Frame(Contact(11, 0.59, 0.92), Contact(12, 0.75, 0.96)), 450);
            TouchpadInteractionFrameResult continued = recognizer.ProcessFrame(
                Frame(Contact(11, 0.62, 0.90), Contact(12, 0.55, 0.92)), 480);
            TouchpadInteractionFrameResult movingFingerPaused = recognizer.ProcessFrame(
                Frame(Contact(12, 0.55, 0.92)), 520);
            TouchpadInteractionFrameResult movingFingerResumed = recognizer.ProcessFrame(
                Frame(Contact(12, 0.7, 0.90), Contact(13, 0.4, 0.4)), 560);

            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(missing.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragPaused, Assert.Single(paused.Events).EventType);
            Assert.True(stillPaused.ClaimInput);
            Assert.Empty(stillPaused.Events);
            Assert.Equal(TouchpadInteractionEventType.WindowDragResumed, Assert.Single(resumed.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(continued.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragPaused, Assert.Single(movingFingerPaused.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragResumed, Assert.Single(movingFingerResumed.Events).EventType);
        }

        [Fact]
        public void LateAnchorReturnRebasesWithoutAnIntermediatePauseFrame()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.BottomEdgeAnchor);
            StartBottomAnchoredDrag(recognizer, 10, 11);

            TouchpadInteractionFrameResult missing = recognizer.ProcessFrame(Frame(Contact(11, 0.55, 0.5)), 150);
            TouchpadInteractionFrameResult resumed = recognizer.ProcessFrame(
                Frame(Contact(11, 0.75, 0.65), Contact(12, 0.6, 0.96)), 400);
            TouchpadInteractionFrameResult continued = recognizer.ProcessFrame(
                Frame(Contact(11, 0.78, 0.67), Contact(12, 0.7, 0.92)), 430);

            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(missing.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragResumed, Assert.Single(resumed.Events).EventType);
            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(continued.Events).EventType);
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

        [Fact]
        public void ThreeFingerModeClaimsTheFrameWhereTheThirdFingerArrives()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);

            TouchpadInteractionFrameResult oneFinger = recognizer.ProcessFrame(Frame(Contact(1, 0.2, 0.3)), 0);
            TouchpadInteractionFrameResult twoFingers = recognizer.ProcessFrame(
                Frame(Contact(1, 0.2, 0.3), Contact(2, 0.5, 0.3)), 20);
            TouchpadInteractionFrameResult threeFingers = recognizer.ProcessFrame(
                Frame(Contact(1, 0.2, 0.3), Contact(2, 0.5, 0.3), Contact(3, 0.8, 0.3)), 40);

            Assert.False(oneFinger.ClaimInput);
            Assert.False(twoFingers.ClaimInput);
            Assert.True(threeFingers.ClaimInput);
            Assert.Empty(threeFingers.Events);
        }

        [Fact]
        public void ThreeFingerTapClaimsWithoutStartingAMouseDrag()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);

            TouchpadInteractionFrameResult pressed = recognizer.ProcessFrame(
                Frame(Contact(1, 0.2, 0.3), Contact(2, 0.5, 0.3), Contact(3, 0.8, 0.3)), 0);
            TouchpadInteractionFrameResult released = recognizer.ProcessFrame(Frame(), 80);

            Assert.True(pressed.ClaimInput);
            Assert.Empty(pressed.Events);
            Assert.True(released.ClaimInput);
            Assert.Empty(released.Events);
            Assert.False(released.SessionActive);
        }

        [Fact]
        public void ThreeFingerCentroidMovementStartsAndMovesDrag()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);
            recognizer.ProcessFrame(
                Frame(Contact(1, 0.2, 0.3), Contact(2, 0.5, 0.3), Contact(3, 0.8, 0.3)), 0);

            TouchpadInteractionFrameResult belowThreshold = recognizer.ProcessFrame(
                Frame(Contact(1, 0.21, 0.3), Contact(2, 0.51, 0.3), Contact(3, 0.81, 0.3)), 20);
            TouchpadInteractionFrameResult started = recognizer.ProcessFrame(
                Frame(Contact(1, 0.23, 0.3), Contact(2, 0.53, 0.3), Contact(3, 0.83, 0.3)), 40);
            TouchpadInteractionFrameResult moved = recognizer.ProcessFrame(
                Frame(Contact(1, 0.25, 0.32), Contact(2, 0.55, 0.32), Contact(3, 0.85, 0.32)), 60);
            TouchpadInteractionFrameResult released = recognizer.ProcessFrame(Frame(), 80);

            Assert.Empty(belowThreshold.Events);
            TouchpadInteractionEvent startedEvent = Assert.Single(started.Events);
            Assert.Equal(TouchpadInteractionEventType.WindowDragStarted, startedEvent.EventType);
            Assert.Equal(0.53, startedEvent.NormalizedX, 3);
            TouchpadInteractionEvent movedEvent = Assert.Single(moved.Events);
            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, movedEvent.EventType);
            Assert.Equal(0.55, movedEvent.NormalizedX, 3);
            Assert.Equal(0.32, movedEvent.NormalizedY, 3);
            Assert.Equal(TouchpadInteractionEventType.WindowDragEnded, Assert.Single(released.Events).EventType);
        }

        [Fact]
        public void ThreeFingerDragPausesAndRebasesWhenAFingerReturns()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);
            StartThreeFingerDrag(recognizer);

            TouchpadInteractionFrameResult paused = recognizer.ProcessFrame(
                Frame(Contact(1, 0.23, 0.3), Contact(2, 0.53, 0.3)), 60);
            TouchpadInteractionFrameResult resumed = recognizer.ProcessFrame(
                Frame(Contact(1, 0.5, 0.5), Contact(2, 0.7, 0.5), Contact(4, 0.9, 0.5)), 80);
            TouchpadInteractionFrameResult moved = recognizer.ProcessFrame(
                Frame(Contact(1, 0.52, 0.51), Contact(2, 0.72, 0.51), Contact(4, 0.92, 0.51)), 100);

            Assert.Equal(TouchpadInteractionEventType.WindowDragPaused, Assert.Single(paused.Events).EventType);
            TouchpadInteractionEvent resumedEvent = Assert.Single(resumed.Events);
            Assert.Equal(TouchpadInteractionEventType.WindowDragResumed, resumedEvent.EventType);
            Assert.Equal(0.7, resumedEvent.NormalizedX, 3);
            Assert.Equal(TouchpadInteractionEventType.WindowDragMoved, Assert.Single(moved.Events).EventType);
        }

        [Fact]
        public void ThreeFingerReplacementInOneFramePausesThenRebases()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);
            StartThreeFingerDrag(recognizer);

            TouchpadInteractionFrameResult replaced = recognizer.ProcessFrame(
                Frame(Contact(1, 0.5, 0.5), Contact(2, 0.7, 0.5), Contact(4, 0.9, 0.5)), 60);

            Assert.Collection(replaced.Events,
                interactionEvent => Assert.Equal(TouchpadInteractionEventType.WindowDragPaused, interactionEvent.EventType),
                interactionEvent => Assert.Equal(TouchpadInteractionEventType.WindowDragResumed, interactionEvent.EventType));
        }

        [Fact]
        public void ThreeFingerModeDoesNotActivateBottomAnchorDrag()
        {
            var recognizer = CreateRecognizer(windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);

            recognizer.ProcessFrame(Frame(Contact(1, 0.5, 0.96)), 0);
            recognizer.ProcessFrame(
                Frame(Contact(1, 0.5, 0.96), Contact(2, 0.5, 0.5)), 110);
            TouchpadInteractionFrameResult moved = recognizer.ProcessFrame(
                Frame(Contact(1, 0.5, 0.96), Contact(2, 0.55, 0.5)), 140);

            Assert.False(moved.ClaimInput);
            Assert.Empty(moved.Events);
        }

        [Fact]
        public void TwoFingerEdgeGestureStillWorksInThreeFingerMode()
        {
            var recognizer = CreateRecognizer(
                FixedEdgeGesture.TwoFingerLeftSwipeIn,
                windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);

            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.3), Contact(2, 0.02, 0.7)), 0);
            TouchpadInteractionFrameResult fired = recognizer.ProcessFrame(
                Frame(Contact(1, 0.14, 0.3), Contact(2, 0.14, 0.7)), 100);

            Assert.True(fired.ClaimInput);
            Assert.Equal(FixedEdgeGesture.TwoFingerLeftSwipeIn, Assert.Single(fired.Events).EdgeGesture);
        }

        [Fact]
        public void ThirdFingerPreemptsUnclaimedTwoFingerEdgeCandidate()
        {
            var recognizer = CreateRecognizer(
                FixedEdgeGesture.TwoFingerLeftSwipeIn,
                windowDragMode: TouchpadWindowDragMode.ThreeFingerDrag);
            recognizer.ProcessFrame(Frame(Contact(1, 0.02, 0.3), Contact(2, 0.02, 0.7)), 0);

            TouchpadInteractionFrameResult thirdFinger = recognizer.ProcessFrame(
                Frame(Contact(1, 0.14, 0.3), Contact(2, 0.14, 0.7), Contact(3, 0.6, 0.5)), 100);

            Assert.True(thirdFinger.ClaimInput);
            Assert.Empty(thirdFinger.Events);
        }

        [Fact]
        public void DisabledWindowDragLeavesThreeFingerInputUnclaimed()
        {
            var recognizer = CreateRecognizer();

            TouchpadInteractionFrameResult result = recognizer.ProcessFrame(
                Frame(Contact(1, 0.2, 0.3), Contact(2, 0.5, 0.3), Contact(3, 0.8, 0.3)), 0);

            Assert.False(result.ClaimInput);
            Assert.Empty(result.Events);
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

        private static void StartThreeFingerDrag(TouchpadInteractionRecognizer recognizer)
        {
            recognizer.ProcessFrame(
                Frame(Contact(1, 0.2, 0.3), Contact(2, 0.5, 0.3), Contact(3, 0.8, 0.3)), 0);
            TouchpadInteractionFrameResult started = recognizer.ProcessFrame(
                Frame(Contact(1, 0.23, 0.3), Contact(2, 0.53, 0.3), Contact(3, 0.83, 0.3)), 40);

            Assert.Equal(TouchpadInteractionEventType.WindowDragStarted, Assert.Single(started.Events).EventType);
        }

        private static TouchpadInteractionRecognizer CreateRecognizer(
            FixedEdgeGesture gesture1 = FixedEdgeGesture.None,
            FixedEdgeGesture gesture2 = FixedEdgeGesture.None,
            FixedEdgeGesture gesture3 = FixedEdgeGesture.None,
            TouchpadWindowDragMode windowDragMode = TouchpadWindowDragMode.Disabled)
        {
            var enabled = new HashSet<FixedEdgeGesture>(new[] { gesture1, gesture2, gesture3 }
                .Where(gesture => gesture != FixedEdgeGesture.None));
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

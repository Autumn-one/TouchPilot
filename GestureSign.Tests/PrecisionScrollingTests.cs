using GestureSign.ControlPanel.Common;
using Xunit;

namespace GestureSign.Tests
{
    public class PrecisionScrollingTests
    {
        [Theory]
        [InlineData(-1, 0.4)]
        [InlineData(-15, 6)]
        [InlineData(-30, 12)]
        [InlineData(-60, 24)]
        [InlineData(-120, 48)]
        [InlineData(-240, 96)]
        [InlineData(120, -48)]
        public void WheelMagnitudeIsPreserved(int wheelDelta, double expectedPixels)
        {
            double actual = PrecisionScrolling.CalculatePixelDelta(wheelDelta, 48);

            Assert.Equal(expectedPixels, actual, 6);
        }

        [Theory]
        [InlineData(-1, true)]
        [InlineData(-60, true)]
        [InlineData(-120, false)]
        [InlineData(-240, false)]
        [InlineData(120, false)]
        public void PartialWheelDeltasAreRecognizedAsHighResolution(int wheelDelta, bool expected)
        {
            Assert.Equal(expected, PrecisionScrolling.IsHighResolutionWheelDelta(wheelDelta));
        }

        [Fact]
        public void SameDirectionWheelInputAccumulatesAtThePendingTarget()
        {
            double target = PrecisionScrolling.CalculateAnimatedTarget(
                currentOffset: 20,
                previousTargetOffset: 68,
                pixelDelta: 48,
                continuesInSameDirection: true,
                scrollableHeight: 500);

            Assert.Equal(116, target);
        }

        [Fact]
        public void ReversedWheelInputRebasesAtTheCurrentOffset()
        {
            double target = PrecisionScrolling.CalculateAnimatedTarget(
                currentOffset: 70,
                previousTargetOffset: 150,
                pixelDelta: -48,
                continuesInSameDirection: false,
                scrollableHeight: 500);

            Assert.Equal(22, target);
        }

        [Theory]
        [InlineData(-10, 0)]
        [InlineData(50, 50)]
        [InlineData(150, 100)]
        public void ScrollTargetsStayWithinTheScrollableRange(double offset, double expected)
        {
            Assert.Equal(expected, PrecisionScrolling.ClampOffset(offset, 100));
        }
    }
}

using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using Xunit;

namespace GestureSign.Tests
{
    public class TouchpadConfidenceTests
    {
        [Fact]
        public void ExistingContactConstructorDefaultsToNotReported()
        {
            var contact = new TouchpadContact(1, DeviceStates.Tip, 0.25, 0.75);

            Assert.Equal(TouchpadContactConfidence.NotReported, contact.Confidence);
            Assert.False(contact.IsLowConfidence);
        }

        [Theory]
        [InlineData(false, new ushort[0], TouchpadContactConfidence.NotReported)]
        [InlineData(true, new ushort[0], TouchpadContactConfidence.LowConfidence)]
        [InlineData(true, new ushort[] { 0x42 }, TouchpadContactConfidence.LowConfidence)]
        [InlineData(true, new ushort[] { 0x42, 0x47 }, TouchpadContactConfidence.Confident)]
        public void HidUsagesResolveConfidence(bool isReported, ushort[] activeUsages,
            TouchpadContactConfidence expected)
        {
            Assert.Equal(expected, TouchPadDevice.ResolveConfidence(isReported, activeUsages));
        }
    }
}

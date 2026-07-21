using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Triggers;
using System.Collections.Generic;
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

        [Fact]
        public void ConfidenceFilterExcludesOnlyActiveLowConfidenceContacts()
        {
            IReadOnlyList<TouchpadContact> contacts = new[]
            {
                Contact(1, DeviceStates.Tip, TouchpadContactConfidence.LowConfidence),
                Contact(2, DeviceStates.Tip, TouchpadContactConfidence.Confident),
                Contact(3, DeviceStates.Tip, TouchpadContactConfidence.NotReported),
                Contact(4, DeviceStates.None, TouchpadContactConfidence.LowConfidence)
            };

            IReadOnlyList<TouchpadContact> filtered = new TouchpadConfidenceContactFilter().Filter(contacts);

            Assert.Collection(filtered,
                contact => Assert.Equal(2, contact.ContactIdentifier),
                contact => Assert.Equal(3, contact.ContactIdentifier),
                contact => Assert.Equal(4, contact.ContactIdentifier));
        }

        [Fact]
        public void ConfidenceFilterReturnsOriginalFrameWhenNoContactIsExcluded()
        {
            IReadOnlyList<TouchpadContact> contacts = new[]
            {
                Contact(1, DeviceStates.Tip, TouchpadContactConfidence.Confident),
                Contact(2, DeviceStates.Tip, TouchpadContactConfidence.NotReported)
            };

            IReadOnlyList<TouchpadContact> filtered = new TouchpadConfidenceContactFilter().Filter(contacts);

            Assert.Same(contacts, filtered);
        }

        private static TouchpadContact Contact(int identifier, DeviceStates state,
            TouchpadContactConfidence confidence)
        {
            return new TouchpadContact(identifier, state, 0.5, 0.5, confidence);
        }
    }
}

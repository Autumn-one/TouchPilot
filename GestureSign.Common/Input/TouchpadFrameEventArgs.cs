using System;
using System.Collections.Generic;

namespace GestureSign.Common.Input
{
    public readonly struct TouchpadContact
    {
        public TouchpadContact(int contactIdentifier, DeviceStates state, double normalizedX, double normalizedY)
        {
            ContactIdentifier = contactIdentifier;
            State = state;
            NormalizedX = normalizedX;
            NormalizedY = normalizedY;
        }

        public int ContactIdentifier { get; }
        public DeviceStates State { get; }
        public double NormalizedX { get; }
        public double NormalizedY { get; }
        public bool IsActive => (State & DeviceStates.Tip) != 0;
    }

    public sealed class TouchpadFrameEventArgs : EventArgs
    {
        public TouchpadFrameEventArgs(IReadOnlyList<TouchpadContact> contacts, long timestampMilliseconds)
        {
            Contacts = contacts ?? throw new ArgumentNullException(nameof(contacts));
            TimestampMilliseconds = timestampMilliseconds;
        }

        public IReadOnlyList<TouchpadContact> Contacts { get; }
        public long TimestampMilliseconds { get; }
        public bool ClaimInput { get; set; }
    }
}

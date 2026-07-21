using System;
using System.Collections.Generic;

namespace GestureSign.Common.Input
{
    public readonly struct TouchpadContact
    {
        public TouchpadContact(int contactIdentifier, DeviceStates state, double normalizedX, double normalizedY)
            : this(contactIdentifier, state, normalizedX, normalizedY, TouchpadContactConfidence.NotReported)
        {
        }

        public TouchpadContact(int contactIdentifier, DeviceStates state, double normalizedX, double normalizedY,
            TouchpadContactConfidence confidence)
        {
            ContactIdentifier = contactIdentifier;
            State = state;
            NormalizedX = normalizedX;
            NormalizedY = normalizedY;
            Confidence = confidence;
        }

        public int ContactIdentifier { get; }
        public DeviceStates State { get; }
        public double NormalizedX { get; }
        public double NormalizedY { get; }
        public TouchpadContactConfidence Confidence { get; }
        public bool IsActive => (State & DeviceStates.Tip) != 0;
        public bool IsLowConfidence => Confidence == TouchpadContactConfidence.LowConfidence;
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

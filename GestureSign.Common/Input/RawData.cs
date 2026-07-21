using System.Drawing;

namespace GestureSign.Common.Input
{

    public struct RawData
    {
        public RawData(DeviceStates state, int contactIdentifier, Point rawPointsData)
            : this(state, contactIdentifier, rawPointsData, double.NaN, double.NaN,
                TouchpadContactConfidence.NotReported)
        {
        }

        public RawData(DeviceStates state, int contactIdentifier, Point rawPointsData, double normalizedX, double normalizedY)
            : this(state, contactIdentifier, rawPointsData, normalizedX, normalizedY,
                TouchpadContactConfidence.NotReported)
        {
        }

        public RawData(DeviceStates state, int contactIdentifier, Point rawPointsData, double normalizedX,
            double normalizedY, TouchpadContactConfidence confidence)
        {
            this.State = state;
            this.ContactIdentifier = contactIdentifier;
            this.RawPoints = rawPointsData;
            this.NormalizedX = normalizedX;
            this.NormalizedY = normalizedY;
            this.Confidence = confidence;
        }
        public DeviceStates State;
        public int ContactIdentifier;
        public Point RawPoints;
        public double NormalizedX;
        public double NormalizedY;
        public TouchpadContactConfidence Confidence;
    }
}

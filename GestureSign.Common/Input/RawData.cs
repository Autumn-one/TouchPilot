using System.Drawing;

namespace GestureSign.Common.Input
{

    public struct RawData
    {
        public RawData(DeviceStates state, int contactIdentifier, Point rawPointsData)
            : this(state, contactIdentifier, rawPointsData, double.NaN, double.NaN)
        {
        }

        public RawData(DeviceStates state, int contactIdentifier, Point rawPointsData, double normalizedX, double normalizedY)
        {
            this.State = state;
            this.ContactIdentifier = contactIdentifier;
            this.RawPoints = rawPointsData;
            this.NormalizedX = normalizedX;
            this.NormalizedY = normalizedY;
        }
        public DeviceStates State;
        public int ContactIdentifier;
        public Point RawPoints;
        public double NormalizedX;
        public double NormalizedY;
    }
}

using GestureSign.Common.Input;
using GestureSign.Daemon.Native;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GestureSign.Daemon.Input
{
    public class TouchPadDevice : HidDevice
    {
        private static readonly ConcurrentDictionary<IntPtr, bool> ConfidenceSupportByDevice =
            new ConcurrentDictionary<IntPtr, bool>();
        private readonly bool _supportsConfidence;

        public override Devices DeviceType => Devices.TouchPad;

        public TouchPadDevice(IntPtr rawInputBuffer, ref RAWINPUT raw) : base(rawInputBuffer, ref raw)
        {
            _supportsConfidence = ConfidenceSupportByDevice.GetOrAdd(raw.header.hDevice,
                deviceHandle => HasInputButtonUsage(NativeMethods.ConfidenceId));
        }

        protected override Point GetCoordinate(short linkCollection, Screen currentScr, IntPtr pRawDataPacket)
        {
            Point physicalPoint = GetPhysicalCoordinate(linkCollection, pRawDataPacket);
            int physicalX = physicalPoint.X;
            int physicalY = physicalPoint.Y;

            int x, y;
            x = physicalX * currentScr.Bounds.Width / _physicalMax.X;
            y = physicalY * currentScr.Bounds.Height / _physicalMax.Y;

            return new Point(x + currentScr.Bounds.X, y + currentScr.Bounds.Y);
        }

        public void GetRawDatas(short numberOfChildren, Screen currentScr, ref int requiringContactCount, ref List<RawData> _outputTouchs)
        {
            for (int dwIndex = 0; dwIndex < _dwCount; dwIndex++)
            {
                IntPtr pRawDataPacket = new IntPtr(_pRawData.ToInt64() + dwIndex * _dwSizHid);
                for (short nodeIndex = 1; nodeIndex <= numberOfChildren; nodeIndex++)
                {
                    int contactIdentifier = GetContactId(nodeIndex, pRawDataPacket);
                    Point physicalPoint = GetPhysicalCoordinate(nodeIndex, pRawDataPacket);
                    int x = physicalPoint.X * currentScr.Bounds.Width / _physicalMax.X;
                    int y = physicalPoint.Y * currentScr.Bounds.Height / _physicalMax.Y;
                    Point point = new Point(x + currentScr.Bounds.X, y + currentScr.Bounds.Y);
                    double normalizedX = NormalizePhysicalCoordinate(physicalPoint.X, _physicalMin.X, _physicalMax.X);
                    double normalizedY = NormalizePhysicalCoordinate(physicalPoint.Y, _physicalMin.Y, _physicalMax.Y);

                    ushort[] usageList = GetButtonList(_hPreparsedData.DangerousGetHandle(), pRawDataPacket,
                        nodeIndex, _dwSizHid);
                    bool tip = Array.IndexOf(usageList, NativeMethods.TipId) >= 0;
                    TouchpadContactConfidence confidence = ResolveConfidence(_supportsConfidence, usageList);

                    _outputTouchs.Add(new RawData(tip ? DeviceStates.Tip : DeviceStates.None,
                        contactIdentifier, point, normalizedX, normalizedY, confidence));

                    if (--requiringContactCount == 0) break;
                }
                if (requiringContactCount == 0) break;
            }
        }

        internal static TouchpadContactConfidence ResolveConfidence(bool isReported, ushort[] activeUsages)
        {
            if (!isReported)
                return TouchpadContactConfidence.NotReported;

            return Array.IndexOf(activeUsages, NativeMethods.ConfidenceId) >= 0
                ? TouchpadContactConfidence.Confident
                : TouchpadContactConfidence.LowConfidence;
        }
    }
}

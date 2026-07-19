using GestureSign.Common.Input;
using GestureSign.Daemon.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GestureSign.Daemon.Input
{
    public class TouchPadDevice : HidDevice
    {
        public override Devices DeviceType => Devices.TouchPad;

        public TouchPadDevice(IntPtr rawInputBuffer, ref RAWINPUT raw) : base(rawInputBuffer, ref raw)
        {
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

                    ushort[] usageList = GetButtonList(_hPreparsedData.DangerousGetHandle(), _pRawData, nodeIndex, _dwSizHid);
                    bool tip = usageList.Length != 0 && usageList[0] == NativeMethods.TipId;

                    _outputTouchs.Add(new RawData(tip ? DeviceStates.Tip : DeviceStates.None, contactIdentifier, point, normalizedX, normalizedY));

                    if (--requiringContactCount == 0) break;
                }
                if (requiringContactCount == 0) break;
            }
        }
    }
}

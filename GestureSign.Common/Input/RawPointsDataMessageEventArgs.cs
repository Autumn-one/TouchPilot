using System;
using System.Collections.Generic;

namespace GestureSign.Common.Input
{
    public class RawPointsDataMessageEventArgs : EventArgs
    {
        #region Constructors

        public RawPointsDataMessageEventArgs(List<RawData> rawData, Devices device)
        {
            this.RawData = rawData;
            SourceDevice = device;
            TimestampMilliseconds = Environment.TickCount64;
        }


        #endregion

        #region Public Properties

        public List<RawData> RawData { get; set; }
        public Devices SourceDevice { get; set; }
        public long TimestampMilliseconds { get; }

        #endregion
    }
}

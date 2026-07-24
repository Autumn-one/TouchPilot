using System;
using System.Runtime.InteropServices;

namespace GestureSign.Daemon.Native
{
    internal static class NativeMethods
    {
        #region const definitions

        internal const int

            WM_PARENTNOTIFY = 0x0210;

        internal const int

            WM_NCPOINTERUPDATE = 0x0241;

        internal const int

            WM_NCPOINTERDOWN = 0x0242;

        internal const int

            WM_NCPOINTERUP = 0x0243;

        internal const int

            WM_POINTERUPDATE = 0x0245;

        internal const int

            WM_POINTERDOWN = 0x0246;

        internal const int

            WM_POINTERUP = 0x0247;

        internal const int

            WM_POINTERENTER = 0x0249;

        internal const int

            WM_POINTERLEAVE = 0x024A;

        internal const int

            WM_POINTERACTIVATE = 0x024B;

        internal const int

            WM_POINTERCAPTURECHANGED = 0x024C;

        internal const int

            WM_POINTERWHEEL = 0x024E;

        internal const int

            WM_POINTERHWHEEL = 0x024F;

        internal const uint ANRUS_TOUCH_MODIFICATION_ACTIVE = 0x0000002;

        internal const int RIDEV_REMOVE = 0x00000001;
        internal const int RIDEV_INPUTSINK = 0x00000100;
        internal const int RIDEV_DEVNOTIFY = 0x00002000;
        internal const int RID_INPUT = 0x10000003;

        internal const int RIM_TYPEHID = 2;

        internal const uint RIDI_DEVICENAME = 0x20000007;
        internal const uint RIDI_DEVICEINFO = 0x2000000b;
        internal const uint RIDI_PREPARSEDDATA = 0x20000005;

        internal const int WM_KEYDOWN = 0x0100;
        internal const int WM_SYSKEYDOWN = 0x0104;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int WM_INPUT = 0x00FF;
        internal const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
        internal const int HTCAPTION = 2;
        internal const uint GA_ROOT = 2;
        internal const int VK_LBUTTON = 0x01;
        internal const int VK_OEM_CLEAR = 0xFE;
        internal const int VK_LAST_KEY = VK_OEM_CLEAR; // this is a made up value used as a sentinal
        internal const uint SMTO_BLOCK = 0x0001;
        internal const uint SMTO_ABORTIFHUNG = 0x0002;

        internal const int WmClose = 0x0010;

        internal const ushort GenericDesktopPage = 0x01;
        internal const ushort DigitizerUsagePage = 0x0D;
        internal const ushort ContactIdentifierId = 0x51;
        internal const ushort ContactCountId = 0x54;
        internal const ushort ScanTimeId = 0x56;
        internal const ushort TipId = 0x42;
        internal const ushort ConfidenceId = 0x47;
        internal const ushort XCoordinateId = 0x30;
        internal const ushort YCoordinateId = 0x31;
        internal const ushort InRangeId = 0x32;
        internal const ushort BarrelButtonId = 0x44;
        internal const ushort InvertId = 0x3C;
        internal const ushort EraserId = 0x45;

        internal const ushort TouchPadUsage = 0x05;
        internal const ushort TouchScreenUsage = 0x04;
        internal const ushort PenUsage = 0x02;

        #endregion const definitions

        #region DllImports


        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr PostMessage(HandleRef hwnd, int msg, int wparam, int lparam);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto)]
        internal static extern int GetWindowThreadProcessId(HandleRef hWnd, out int lpdwProcessId);

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Auto)]
        internal static extern int GetCurrentThreadId();

        [DllImport("user32.dll")]
        internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("User32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterPointerInputTarget(IntPtr handle, POINTER_INPUT_TYPE pointerType);

        [DllImport("User32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterPointerInputTarget(IntPtr hwnd, POINTER_INPUT_TYPE pointerType);

        [DllImport("Oleacc.dll")]
        internal static extern int AccSetRunningUtilityState(IntPtr hWnd, uint dwUtilityStateMask, uint dwUtilityState);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetPointerFrameInfo(int pointerID, ref int pointerCount, [MarshalAs(UnmanagedType.LPArray), In, Out] POINTER_INFO[] pointerInfo);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool InitializeTouchInjection(int maxCount, TOUCH_FEEDBACK feedbackMode);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool InjectTouchInput(int count, [MarshalAs(UnmanagedType.LPArray), In] POINTER_TOUCH_INFO[] contacts);

        [DllImport("User32.dll")]
        internal static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("User32.dll")]
        internal static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevice, uint uiNumDevices, uint cbSize);

        [DllImport("User32.dll")]
        internal static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint uiNumDevices, uint cbSize);

        [DllImport("User32.dll")]
        internal static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [DllImport("User32.dll")]
        public static extern IntPtr MonitorFromPoint([In] System.Drawing.Point pt, [In] uint dwFlags);

        [DllImport("Shcore.dll")]
        public static extern IntPtr GetDpiForMonitor([In] IntPtr hmonitor, [In] MonitorDpiType dpiType, [Out] out uint dpiX, [Out] out uint dpiY);


        #endregion DllImports

        private const string User32Dll = "user32.dll";

        /// <summary>
        /// Represents the different types of scaling.
        /// </summary>
        /// <seealso cref="https://msdn.microsoft.com/en-us/library/windows/desktop/dn280511.aspx"/>
        public enum MonitorDpiType
        {
            Effective = 0,
            Angular = 1,
            Raw = 2,
        }

        [DllImport(User32Dll, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport(User32Dll)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport(User32Dll)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport(User32Dll, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(int idAttach, int idAttachTo,
            [MarshalAs(UnmanagedType.Bool)] bool attach);

        [DllImport(User32Dll, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport(User32Dll)]
        public static extern short GetAsyncKeyState(int virtualKey);

        [DllImport(User32Dll)]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [DllImport(User32Dll, SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

    }
}

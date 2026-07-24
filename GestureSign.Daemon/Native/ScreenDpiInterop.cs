using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;

namespace GestureSign.Daemon.Native
{
    internal static class ScreenDpiInterop
    {
        private const int LogicalPixelsX = 88;

        internal static int GetSystemDpi()
        {
            IntPtr handle = GetDesktopDeviceContext(IntPtr.Zero);
            if (handle == IntPtr.Zero)
                return 0;

            using (var deviceContext = new DesktopDeviceContextHandle(handle))
                return GetDeviceCapability(deviceContext.DangerousGetHandle(), LogicalPixelsX);
        }

        [DllImport("user32.dll", EntryPoint = "GetDC", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetDesktopDeviceContext(IntPtr windowHandle);

        [DllImport("user32.dll", EntryPoint = "ReleaseDC", ExactSpelling = true)]
        private static extern int ReleaseDeviceContext(IntPtr windowHandle, IntPtr deviceContext);

        [DllImport("gdi32.dll", EntryPoint = "GetDeviceCaps", ExactSpelling = true)]
        private static extern int GetDeviceCapability(IntPtr deviceContext, int capability);

        private sealed class DesktopDeviceContextHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal DesktopDeviceContextHandle(IntPtr handle)
                : base(true)
            {
                SetHandle(handle);
            }

            protected override bool ReleaseHandle()
            {
                return ReleaseDeviceContext(IntPtr.Zero, handle) != 0;
            }
        }
    }
}

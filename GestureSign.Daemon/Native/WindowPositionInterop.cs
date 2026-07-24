using System;
using System.Runtime.InteropServices;

namespace GestureSign.Daemon.Native
{
    [Flags]
    internal enum WindowPositionFlags : uint
    {
        NoSize = 0x0001,
        NoMove = 0x0002,
        NoZOrder = 0x0004,
        NoActivate = 0x0010,
        ShowWindow = 0x0040,
        NoOwnerZOrder = 0x0200,
        AsyncWindowPosition = 0x4000
    }

    internal static class WindowPositionInterop
    {
        private const string User32Library = "user32.dll";

        [DllImport(User32Library, EntryPoint = "SetWindowPos", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPosition(IntPtr windowHandle, IntPtr insertAfter,
            int x, int y, int width, int height, WindowPositionFlags flags);

        internal static IntPtr GetWindowAt(int x, int y)
        {
            return WindowFromPoint(new NativePoint(x, y));
        }

        [DllImport(User32Library, ExactSpelling = true)]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativePoint
        {
            internal readonly int X;
            internal readonly int Y;

            internal NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }
        }
    }
}

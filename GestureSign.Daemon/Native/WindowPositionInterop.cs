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
        private const string DwmApiLibrary = "dwmapi.dll";

        [DllImport(User32Library, EntryPoint = "SetWindowPos", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPosition(IntPtr windowHandle, IntPtr insertAfter,
            int x, int y, int width, int height, WindowPositionFlags flags);

        [DllImport(User32Library, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsHungAppWindow(IntPtr windowHandle);

        [DllImport(User32Library, EntryPoint = "GetWindowRect", ExactSpelling = true,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRectangle(IntPtr windowHandle,
            out NativeWindowRectangle rectangle);

        [DllImport(User32Library, EntryPoint = "PostMessageW", ExactSpelling = true,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostWindowMessage(IntPtr windowHandle, uint message,
            IntPtr wParam, IntPtr lParam);

        [DllImport(User32Library, EntryPoint = "SendMessageW", ExactSpelling = true,
            SetLastError = true)]
        internal static extern IntPtr SendWindowMessage(IntPtr windowHandle, uint message,
            IntPtr wParam, IntPtr lParam);

        [DllImport(DwmApiLibrary, EntryPoint = "DwmFlush", ExactSpelling = true)]
        internal static extern int FlushDesktopComposition();

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

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct NativeWindowRectangle
        {
            internal readonly int Left;
            internal readonly int Top;
            internal readonly int Right;
            internal readonly int Bottom;
        }
    }
}

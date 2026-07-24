using Microsoft.Win32.SafeHandles;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GestureSign.Daemon.Surface
{
    internal sealed class LayeredWindowSurface : IDisposable
    {
        private const uint DibRgbColors = 0;
        private const uint BitmapCompressionRgb = 0;
        private const uint UpdateLayeredWindowAlpha = 0x00000002;
        private const byte SourceOver = 0x00;
        private const byte SourceAlpha = 0x01;

        private SurfaceHandle _surfaceHandle;
        private Graphics _graphics;

        public LayeredWindowSurface(Size size)
        {
            if (size.Width <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), "The surface width must be positive.");
            if (size.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), "The surface height must be positive.");

            Size = size;
            _surfaceHandle = CreateSurface(size);
            try
            {
                _graphics = Graphics.FromHdc(_surfaceHandle.DangerousGetHandle());
                _graphics.CompositingQuality = CompositingQuality.HighSpeed;
                _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            }
            catch
            {
                _surfaceHandle.Dispose();
                _surfaceHandle = null;
                throw;
            }
        }

        public Size Size { get; }

        internal IntPtr BitmapHandle
        {
            get
            {
                ThrowIfDisposed();
                return _surfaceHandle.BitmapHandle;
            }
        }

        public void Draw(Action<Graphics> draw)
        {
            if (draw == null)
                throw new ArgumentNullException(nameof(draw));

            ThrowIfDisposed();
            draw(_graphics);
            _graphics.Flush(FlushIntention.Sync);
        }

        public unsafe bool Present(IntPtr windowHandle, Rectangle windowBounds,
            Rectangle dirtyRectangle, byte opacity)
        {
            if (windowHandle == IntPtr.Zero)
                throw new ArgumentException("A valid layered window handle is required.", nameof(windowHandle));

            ThrowIfDisposed();

            var destination = new NativePoint(windowBounds.X, windowBounds.Y);
            var source = new NativePoint(0, 0);
            var windowSize = new NativeSize(windowBounds.Width, windowBounds.Height);
            var dirty = new NativeRectangle(dirtyRectangle.Left, dirtyRectangle.Top,
                dirtyRectangle.Right, dirtyRectangle.Bottom);
            var blend = new BlendFunction
            {
                BlendOperation = SourceOver,
                BlendFlags = 0,
                SourceConstantAlpha = opacity,
                AlphaFormat = SourceAlpha
            };
            var update = new UpdateLayeredWindowInfo
            {
                Size = (uint)Marshal.SizeOf<UpdateLayeredWindowInfo>(),
                DestinationDc = IntPtr.Zero,
                Destination = &destination,
                WindowSize = &windowSize,
                SourceDc = _surfaceHandle.DangerousGetHandle(),
                Source = &source,
                ColorKey = 0,
                Blend = &blend,
                Flags = UpdateLayeredWindowAlpha,
                DirtyRectangle = &dirty
            };

            bool updated = NativeMethods.UpdateLayeredWindowIndirect(windowHandle, ref update);
            GC.KeepAlive(_surfaceHandle);
            return updated;
        }

        public void Dispose()
        {
            Graphics graphics = _graphics;
            _graphics = null;
            graphics?.Dispose();

            SurfaceHandle surfaceHandle = _surfaceHandle;
            _surfaceHandle = null;
            surfaceHandle?.Dispose();
        }

        private static SurfaceHandle CreateSurface(Size size)
        {
            IntPtr memoryDc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            if (memoryDc == IntPtr.Zero)
                throw CreateNativeException("CreateCompatibleDC");

            IntPtr bitmap = IntPtr.Zero;
            IntPtr previousBitmap = IntPtr.Zero;
            try
            {
                var bitmapInfo = new BitmapInfo
                {
                    Header = new BitmapInfoHeader
                    {
                        Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                        Width = size.Width,
                        Height = size.Height,
                        Planes = 1,
                        BitsPerPixel = 32,
                        Compression = BitmapCompressionRgb
                    }
                };

                bitmap = NativeMethods.CreateDIBSection(memoryDc, ref bitmapInfo, DibRgbColors,
                    out IntPtr bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                    throw CreateNativeException("CreateDIBSection");

                previousBitmap = NativeMethods.SelectObject(memoryDc, bitmap);
                if (previousBitmap == IntPtr.Zero)
                    throw CreateNativeException("SelectObject");

                return new SurfaceHandle(memoryDc, bitmap, previousBitmap);
            }
            catch
            {
                if (previousBitmap != IntPtr.Zero)
                    NativeMethods.SelectObject(memoryDc, previousBitmap);
                if (bitmap != IntPtr.Zero)
                    NativeMethods.DeleteObject(bitmap);
                NativeMethods.DeleteDC(memoryDc);
                throw;
            }
        }

        private static ApplicationException CreateNativeException(string operation)
        {
            int error = Marshal.GetLastWin32Error();
            return new ApplicationException($"{operation} failed with Win32 error {error}.");
        }

        private void ThrowIfDisposed()
        {
            if (_surfaceHandle == null || _surfaceHandle.IsClosed)
                throw new ObjectDisposedException(nameof(LayeredWindowSurface));
        }

        private sealed class SurfaceHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private IntPtr _bitmap;
            private readonly IntPtr _previousBitmap;

            public SurfaceHandle(IntPtr memoryDc, IntPtr bitmap, IntPtr previousBitmap)
                : base(true)
            {
                SetHandle(memoryDc);
                _bitmap = bitmap;
                _previousBitmap = previousBitmap;
            }

            public IntPtr BitmapHandle => _bitmap;

            protected override bool ReleaseHandle()
            {
                bool restored = _previousBitmap == IntPtr.Zero ||
                                NativeMethods.SelectObject(handle, _previousBitmap) != IntPtr.Zero;
                bool bitmapDeleted = _bitmap == IntPtr.Zero || NativeMethods.DeleteObject(_bitmap);
                bool dcDeleted = NativeMethods.DeleteDC(handle);
                _bitmap = IntPtr.Zero;
                return restored && bitmapDeleted && dcDeleted;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitsPerPixel;
            public uint Compression;
            public uint ImageSize;
            public int HorizontalPixelsPerMeter;
            public int VerticalPixelsPerMeter;
            public uint ColorsUsed;
            public uint ImportantColors;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader Header;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativePoint
        {
            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }

            public readonly int X;
            public readonly int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativeSize
        {
            public NativeSize(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public readonly int Width;
            public readonly int Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativeRectangle
        {
            public NativeRectangle(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            public readonly int Left;
            public readonly int Top;
            public readonly int Right;
            public readonly int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction
        {
            public byte BlendOperation;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct UpdateLayeredWindowInfo
        {
            public uint Size;
            public IntPtr DestinationDc;
            public NativePoint* Destination;
            public NativeSize* WindowSize;
            public IntPtr SourceDc;
            public NativePoint* Source;
            public uint ColorKey;
            public BlendFunction* Blend;
            public uint Flags;
            public NativeRectangle* DirtyRectangle;
        }

        private static class NativeMethods
        {
            [DllImport("gdi32.dll", SetLastError = true)]
            public static extern IntPtr CreateCompatibleDC(IntPtr dc);

            [DllImport("gdi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteDC(IntPtr dc);

            [DllImport("gdi32.dll", SetLastError = true)]
            public static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo bitmapInfo,
                uint usage, out IntPtr bits, IntPtr section, uint offset);

            [DllImport("gdi32.dll", SetLastError = true)]
            public static extern IntPtr SelectObject(IntPtr dc, IntPtr graphicObject);

            [DllImport("gdi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteObject(IntPtr graphicObject);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UpdateLayeredWindowIndirect(IntPtr window,
                ref UpdateLayeredWindowInfo updateInfo);
        }
    }
}

using GestureSign.Daemon.Surface;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Xunit;

namespace GestureSign.Tests
{
    [Collection(DesktopInputIntegrationCollection.Name)]
    public class GestureTrailSurfaceTests
    {
        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void NativeSurfaceRetainsPerPixelAlpha()
        {
            using (var surface = new DiBitmap(new Size(12, 10)))
            {
                Graphics graphics = surface.BeginDraw();
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Color.FromArgb(128, 40, 80, 120)))
                    graphics.FillRectangle(brush, 2, 2, 6, 6);
                surface.EndDraw();

                NativeBitmap bitmap = GetNativeBitmap(surface.HBitmap);
                Assert.Equal(12, bitmap.Width);
                Assert.Equal(10, Math.Abs(bitmap.Height));
                Assert.Equal(32, bitmap.BitsPerPixel);
                Assert.NotEqual(IntPtr.Zero, bitmap.Bits);

                Color pixel = ReadPixel(bitmap, 4, 4);
                Assert.True(pixel.A == 128, $"Unexpected native pixel value: {pixel}.");
                Assert.InRange(pixel.R, 19, 20);
                Assert.InRange(pixel.G, 39, 40);
                Assert.InRange(pixel.B, 59, 60);
            }
        }

        [Fact]
        [Trait("Category", "WindowsIntegration")]
        public void NativeSurfaceReleasesGdiObjectsAfterRepeatedUse()
        {
            ExerciseSurface();
            int initialCount = GetGuiResources(Process.GetCurrentProcess().Handle, GdiObjects);
            for (int index = 0; index < 32; index++)
                ExerciseSurface();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            int finalCount = GetGuiResources(Process.GetCurrentProcess().Handle, GdiObjects);
            Assert.InRange(finalCount - initialCount, 0, 2);
        }

        private static void ExerciseSurface()
        {
            using (var surface = new DiBitmap(new Size(32, 24)))
            {
                Graphics graphics = surface.BeginDraw();
                graphics.Clear(Color.Transparent);
                graphics.DrawLine(Pens.White, 0, 0, 31, 23);
                surface.EndDraw();
            }
        }

        private static NativeBitmap GetNativeBitmap(IntPtr handle)
        {
            int result = GetObject(handle, Marshal.SizeOf<NativeBitmap>(), out NativeBitmap bitmap);
            Assert.Equal(Marshal.SizeOf<NativeBitmap>(), result);
            return bitmap;
        }

        private static Color ReadPixel(NativeBitmap bitmap, int x, int y)
        {
            int row = bitmap.Height < 0 ? y : bitmap.Height - y - 1;
            int value = Marshal.ReadInt32(bitmap.Bits, row * bitmap.WidthBytes + x * 4);
            return Color.FromArgb(
                (value >> 24) & 0xff,
                (value >> 16) & 0xff,
                (value >> 8) & 0xff,
                value & 0xff);
        }

        private const uint GdiObjects = 0;

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)]
        private static extern int GetObject(IntPtr handle, int bufferSize, out NativeBitmap bitmap);

        [DllImport("user32.dll")]
        private static extern int GetGuiResources(IntPtr process, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeBitmap
        {
            public int Type;
            public int Width;
            public int Height;
            public int WidthBytes;
            public ushort Planes;
            public ushort BitsPerPixel;
            public IntPtr Bits;
        }
    }
}

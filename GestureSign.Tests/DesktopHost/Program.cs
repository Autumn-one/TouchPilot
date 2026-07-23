using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace GestureSign.Tests.DesktopHost
{
    internal static class Program
    {
        private const byte VirtualKeyControl = 0x11;
        private const uint KeyEventKeyUp = 0x0002;
        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;
        private const uint GetAncestorRoot = 2;
        private const string WindowTitle = "TouchPilot external foreground integration test";

        private static bool _controlPressed;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (!TryParseArguments(args, out Rectangle bounds, out bool holdControl))
                {
                    Console.Error.WriteLine("Expected x, y, width, height, and holdControl arguments.");
                    return 2;
                }

                using (var form = new Form
                {
                    Bounds = bounds,
                    FormBorderStyle = FormBorderStyle.FixedToolWindow,
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    TopMost = true,
                    Text = WindowTitle
                })
                {
                    form.FormClosed += (sender, eventArgs) => ReleaseControlKey();
                    form.Shown += (sender, eventArgs) => form.BeginInvoke(new Action(() =>
                        ActivateAndReport(form, bounds, holdControl)));
                    Application.Run(form);
                }

                return 0;
            }
            catch (Exception exception)
            {
                ReleaseControlKey();
                Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
                return 1;
            }
        }

        private static void ActivateAndReport(Form form, Rectangle bounds, bool holdControl)
        {
            try
            {
                Application.DoEvents();
                int cursorX = bounds.Right - 30;
                int cursorY = bounds.Top + Math.Min(80, bounds.Height - 30);
                var point = new NativePoint(cursorX, cursorY);
                IntPtr hitWindow = IntPtr.Zero;

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    SetCursorPos(cursorX, cursorY);
                    Application.DoEvents();
                    hitWindow = GetAncestor(WindowFromPoint(point), GetAncestorRoot);
                    if (hitWindow == form.Handle)
                        break;

                    Thread.Sleep(20);
                }

                mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                Application.DoEvents();
                SetForegroundWindow(form.Handle);
                Application.DoEvents();
                IntPtr foregroundWindow = GetForegroundWindow();

                if (holdControl)
                {
                    keybd_event(VirtualKeyControl, 0, 0, UIntPtr.Zero);
                    _controlPressed = true;
                    Application.DoEvents();
                }

                if (foregroundWindow == form.Handle)
                {
                    form.TopMost = false;
                    Application.DoEvents();
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}",
                    form.Handle.ToInt64(), hitWindow.ToInt64(), foregroundWindow.ToInt64()));
                Console.Out.Flush();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
                form.Close();
            }
        }

        private static bool TryParseArguments(string[] args, out Rectangle bounds, out bool holdControl)
        {
            bounds = Rectangle.Empty;
            holdControl = false;
            if (args.Length != 5 ||
                !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
                !int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) ||
                !bool.TryParse(args[4], out holdControl) ||
                width <= 0 || height <= 0)
            {
                return false;
            }

            bounds = new Rectangle(x, y, width, height);
            return true;
        }

        private static void ReleaseControlKey()
        {
            if (!_controlPressed)
                return;

            keybd_event(VirtualKeyControl, 0, KeyEventKeyUp, UIntPtr.Zero);
            _controlPressed = false;
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

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    }
}

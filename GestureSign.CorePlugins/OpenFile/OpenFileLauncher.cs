using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace GestureSign.CorePlugins.OpenFile
{
    internal static class OpenFileLauncher
    {
        private const int ErrorCancelled = 1223;

        internal static bool Open(string path, string arguments, bool runAsAdministrator)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A file or website is required.", nameof(path));

            try
            {
                using (var currentProcess = Process.GetCurrentProcess())
                {
                    if (!runAsAdministrator && IsElevated(currentProcess))
                    {
                        OpenWithDesktopShell(path, arguments, Environment.CurrentDirectory);
                        return true;
                    }
                }

                using (Process.Start(new ProcessStartInfo(path, arguments ?? string.Empty)
                {
                    UseShellExecute = true,
                    Verb = runAsAdministrator ? "runas" : string.Empty
                }))
                {
                }
                return true;
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
            {
                return false;
            }
        }

        internal static void OpenWithDesktopShell(string path, string arguments, string workingDirectory)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                ExecuteInDesktopShell(path, arguments, workingDirectory);
                return;
            }

            // Gesture commands run on the thread pool; Shell automation requires an STA.
            ExceptionDispatchInfo failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    ExecuteInDesktopShell(path, arguments, workingDirectory);
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            }) { IsBackground = true, Name = "TouchPilot open file" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            failure?.Throw();
        }

        private static void ExecuteInDesktopShell(string path, string arguments, string workingDirectory)
        {
            object windows = null;
            object desktop = null;
            object folderView = null;
            object shell = null;
            try
            {
                // Obtain the existing desktop's Shell, not a new Shell.Application in our token.
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(
                    new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), throwOnError: true));
                object location = 0;
                object root = 0;
                int windowHandle;
                desktop = ((dynamic)windows).FindWindowSW(ref location, ref root,
                    8 /* SWC_DESKTOP */, out windowHandle, 1 /* SWFO_NEEDDISPATCH */);
                if (desktop == null || windowHandle == 0 ||
                    GetWindowThreadProcessId(new IntPtr(windowHandle), out uint processId) == 0)
                    throw new InvalidOperationException("The Windows desktop is unavailable for a normal-permission launch.");

                using (var desktopProcess = Process.GetProcessById(checked((int)processId)))
                {
                    if (IsElevated(desktopProcess))
                        throw new InvalidOperationException("The Windows desktop is elevated; a normal-permission launch is unavailable.");
                }

                folderView = ((dynamic)desktop).Document;
                shell = ((dynamic)folderView).Application;
                ((dynamic)shell).ShellExecute(path, arguments ?? string.Empty, workingDirectory,
                    string.Empty, 1 /* SW_SHOWNORMAL */);
            }
            finally
            {
                ReleaseComObject(shell);
                ReleaseComObject(folderView);
                ReleaseComObject(desktop);
                ReleaseComObject(windows);
            }
        }

        private static bool IsElevated(Process process)
        {
            if (!OpenProcessToken(process.SafeHandle, 0x0008 /* TOKEN_QUERY */, out var token))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            using (token)
            {
                if (!GetTokenInformation(token, 20 /* TokenElevation */, out int elevated,
                    sizeof(int), out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return elevated != 0;
            }
        }

        private static void ReleaseComObject(object instance)
        {
            if (instance != null && Marshal.IsComObject(instance))
                Marshal.ReleaseComObject(instance);
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(SafeProcessHandle processHandle, uint desiredAccess,
            out SafeAccessTokenHandle tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(SafeAccessTokenHandle tokenHandle,
            int informationClass, out int information, int informationLength, out int returnLength);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
    }
}

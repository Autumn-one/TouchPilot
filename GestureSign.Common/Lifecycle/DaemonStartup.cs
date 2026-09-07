using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace GestureSign.Common.Lifecycle
{
    public static class DaemonStartup
    {
        public static bool IsCurrentProcessElevated
        {
            get
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static bool NeedsElevation(bool configured, bool elevated, bool uiAccess)
        {
            return configured && !elevated && !uiAccess;
        }

        public static ProcessStartInfo CreateStartInfo(string executablePath, bool elevate)
        {
            string fullPath = Path.GetFullPath(executablePath);
            return new ProcessStartInfo(fullPath)
            {
                WorkingDirectory = Path.GetDirectoryName(fullPath),
                UseShellExecute = elevate,
                Verb = elevate ? "runas" : string.Empty
            };
        }

        public static bool IsRunning()
        {
            try
            {
                using Mutex mutex = Mutex.OpenExisting(Constants.Daemon);
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // An elevated instance can deny full access to its single-instance mutex.
                return true;
            }
        }

        public static bool TryGetRunningElevation(string directory, out bool elevated)
        {
            string daemonPath = Path.GetFullPath(Path.Combine(directory, Constants.DaemonFileName));
            string legacyPath = Path.GetFullPath(Path.Combine(directory, "GestureSign.exe"));
            using Process current = Process.GetCurrentProcess();
            foreach (string name in new[] { Path.GetFileNameWithoutExtension(Constants.DaemonFileName), "GestureSign" })
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        try
                        {
                            if (process.Id == current.Id || process.SessionId != current.SessionId)
                                continue;
                            using SafeProcessHandle handle = OpenProcess(0x1000, false, process.Id);
                            if (handle.IsInvalid)
                                continue;
                            var path = new StringBuilder(32768);
                            int length = path.Capacity;
                            if (!QueryFullProcessImageName(handle, 0, path, ref length) ||
                                !(string.Equals(path.ToString(), daemonPath, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(path.ToString(), legacyPath, StringComparison.OrdinalIgnoreCase)))
                                continue;
                            if (OpenProcessToken(handle, 0x0008, out SafeAccessTokenHandle token))
                            {
                                using (token)
                                {
                                    if (GetTokenInformation(token, 20, out int value, sizeof(int), out _))
                                    {
                                        elevated = value != 0;
                                        return true;
                                    }
                                }
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // The process exited during inspection.
                        }
                    }
                }
            }
            elevated = false;
            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags,
            StringBuilder path, ref int size);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(SafeProcessHandle process, uint access,
            out SafeAccessTokenHandle token);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
            out int value, int length, out int returnLength);
    }
}

using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GestureSign.ReleaseManager
{
    internal sealed class ReleaseProcessJob : IDisposable
    {
        private const uint CancellationExitCode = 1;
        private SafeFileHandle _handle;

        private ReleaseProcessJob(SafeFileHandle handle)
        {
            _handle = handle;
        }

        public static ReleaseProcessJob TryAssign(Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));
            if (!OperatingSystem.IsWindows())
                return null;

            SafeFileHandle handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (handle == null || handle.IsInvalid)
            {
                handle?.Dispose();
                return null;
            }

            try
            {
                if (!SetKillOnClose(handle) ||
                    !NativeMethods.AssignProcessToJobObject(handle, process.SafeHandle))
                {
                    handle.Dispose();
                    return null;
                }

                return new ReleaseProcessJob(handle);
            }
            catch (InvalidOperationException)
            {
                handle.Dispose();
                return null;
            }
        }

        public bool TryTerminate()
        {
            SafeFileHandle handle = _handle;
            return handle != null && !handle.IsInvalid && !handle.IsClosed &&
                   NativeMethods.TerminateJobObject(handle, CancellationExitCode);
        }

        public void Dispose()
        {
            SafeFileHandle handle = _handle;
            _handle = null;
            handle?.Dispose();
        }

        private static bool SetKillOnClose(SafeFileHandle handle)
        {
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitFlags.KillOnJobClose
                }
            };
            int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(information, buffer, false);
                return NativeMethods.SetInformationJobObject(handle,
                    JobObjectInformationClass.ExtendedLimitInformation, buffer, (uint)size);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [Flags]
        private enum JobObjectLimitFlags : uint
        {
            KillOnJobClose = 0x00002000
        }

        private enum JobObjectInformationClass
        {
            ExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public JobObjectLimitFlags LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetInformationJobObject(SafeFileHandle job, JobObjectInformationClass infoClass,
                IntPtr information, uint informationLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
        }
    }
}

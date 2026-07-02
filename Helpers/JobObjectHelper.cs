using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BlueSapphire.Helpers
{
    /// <summary>
    /// Win32 作业对象 (Job Object) 辅助工具类。
    /// 用于将外部生成的子进程（如 MCP 后台 Node/Python 进程）绑定到主应用程序的作业中。
    /// 开启 JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE 后，一旦主程序（BlueSapphire）异常退出、关闭或被结束进程，
    /// 操作系统内核会自动级联清退所有绑定的子进程，彻底解决孤儿进程与资源泄漏问题。
    /// </summary>
    public static class JobObjectHelper
    {
        private static readonly IntPtr _jobHandle = IntPtr.Zero;

        static JobObjectHelper()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    _jobHandle = CreateJobObject(IntPtr.Zero, null);
                    if (_jobHandle != IntPtr.Zero)
                    {
                        var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                        {
                            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        };
                        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                        {
                            BasicLimitInformation = info
                        };

                        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                        IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
                        try
                        {
                            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
                            SetInformationJobObject(_jobHandle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(extendedInfoPtr);
                        }
                    }
                }
            }
            catch
            {
                // 如果在非 Windows 环境或权限受限下失败，静默降级，避免阻断主业务流程
            }
        }

        /// <summary>
        /// 将指定进程绑定至全局清理作业对象。
        /// </summary>
        public static void AssignProcess(Process? process)
        {
            if (process == null || _jobHandle == IntPtr.Zero || !OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    AssignProcessToJobObject(_jobHandle, process.Handle);
                }
            }
            catch
            {
                // 进程可能已经瞬间退出，或遇到访问权限限制，忽略即可
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        private enum JobObjectInfoType
        {
            AssociateCompletionPortInformation = 7,
            BasicLimitInformation = 2,
            BasicUIRestrictions = 4,
            EndOfJobTimeInformation = 6,
            ExtendedLimitInformation = 9,
            SecurityLimitInformation = 5,
            GroupInformation = 11
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}

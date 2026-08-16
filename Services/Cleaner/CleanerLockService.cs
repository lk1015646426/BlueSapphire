using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace BlueSapphire.Services.Cleaner
{
    public sealed class CleanerLockService
    {
        private const int CchRmSessionKey = 32;
        private const int ErrorMoreData = 234;

        public IReadOnlyList<string> GetLockingProcesses(IEnumerable<string> paths)
        {
            List<string> normalizedPaths = paths
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            if (normalizedPaths.Count == 0)
            {
                return Array.Empty<string>();
            }

            uint sessionHandle = 0;
            string key = Guid.NewGuid().ToString("N")[..Math.Min(CchRmSessionKey, 32)];

            try
            {
                int startResult = RmStartSession(out sessionHandle, 0, key);
                if (startResult != 0)
                {
                    return Array.Empty<string>();
                }

                int registerResult = RmRegisterResources(sessionHandle, (uint)normalizedPaths.Count, normalizedPaths.ToArray(), 0, null, 0, null);
                if (registerResult != 0)
                {
                    return Array.Empty<string>();
                }

                uint procInfoNeeded = 0;
                uint procInfo = 0;
                uint rebootReasons = 0;
                int listResult = RmGetList(sessionHandle, out procInfoNeeded, ref procInfo, null, ref rebootReasons);
                if (listResult == ErrorMoreData)
                {
                    RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[procInfoNeeded];
                    procInfo = procInfoNeeded;
                    listResult = RmGetList(sessionHandle, out procInfoNeeded, ref procInfo, processInfo, ref rebootReasons);
                    if (listResult == 0)
                    {
                        return processInfo
                            .Take((int)procInfo)
                            .Select(ResolveDisplayName)
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                }

                return Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
            finally
            {
                if (sessionHandle != 0)
                {
                    RmEndSession(sessionHandle);
                }
            }
        }

        private static string ResolveDisplayName(RM_PROCESS_INFO info)
        {
            try
            {
                if (info.Process.dwProcessId <= 0)
                {
                    return info.strAppName ?? string.Empty;
                }

                using Process process = Process.GetProcessById(info.Process.dwProcessId);
                return string.IsNullOrWhiteSpace(process.ProcessName)
                    ? info.strAppName ?? string.Empty
                    : process.ProcessName;
            }
            catch
            {
                return info.strAppName ?? string.Empty;
            }
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint pSessionHandle,
            uint nFiles,
            string[]? rgsFilenames,
            uint nApplications,
            [In] RM_UNIQUE_PROCESS[]? rgApplications,
            uint nServices,
            string[]? rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
            ref uint lpdwRebootReasons);

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        private enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;

            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;

            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }
    }
}

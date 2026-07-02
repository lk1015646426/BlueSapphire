using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public class NativeFileService
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_NOERRORUI = 0x0400;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOperation);

        public async Task<bool> MoveToRecycleBinAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var operation = new SHFILEOPSTRUCT
                    {
                        wFunc = FO_DELETE,
                        pFrom = filePath + "\0\0",
                        fFlags = FOF_SILENT | FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI
                    };

                    return SHFileOperation(ref operation) == 0 && !operation.fAnyOperationsAborted;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<List<string>> MoveToRecycleBinBatchAsync(IEnumerable<string> filePaths)
        {
            return await Task.Run(() =>
            {
                var successfulPaths = new List<string>();
                var list = filePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (list.Count == 0) return successfulPaths;

                try
                {
                    string pFrom = string.Join("\0", list) + "\0\0";
                    var operation = new SHFILEOPSTRUCT
                    {
                        wFunc = FO_DELETE,
                        pFrom = pFrom,
                        fFlags = FOF_SILENT | FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI
                    };

                    if (SHFileOperation(ref operation) == 0 && !operation.fAnyOperationsAborted)
                    {
                        successfulPaths.AddRange(list);
                        return successfulPaths;
                    }
                }
                catch
                {
                    // Ignore exception, fallback below
                }

                foreach (var path in list)
                {
                    try
                    {
                        var singleOp = new SHFILEOPSTRUCT
                        {
                            wFunc = FO_DELETE,
                            pFrom = path + "\0\0",
                            fFlags = FOF_SILENT | FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI
                        };

                        if (SHFileOperation(ref singleOp) == 0 && !singleOp.fAnyOperationsAborted)
                        {
                            successfulPaths.Add(path);
                        }
                    }
                    catch
                    {
                        // Ignore exception
                    }
                }

                return successfulPaths;
            });
        }

        public async Task<bool> RevealInExplorerAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = true
                    };

                    using var process = Process.Start(startInfo);
                    return process != null;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> OpenFolderAsync(string folderPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{folderPath}\"",
                        UseShellExecute = true
                    };

                    using var process = Process.Start(startInfo);
                    return process != null;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}

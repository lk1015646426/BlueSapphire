using System;
using System.Diagnostics;
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
                        fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI
                    };

                    return SHFileOperation(ref operation) == 0;
                }
                catch
                {
                    return false;
                }
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

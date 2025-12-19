using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public class NativeFileService
    {
        // 定义 Windows Shell API 结构体
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
        private const ushort FOF_ALLOWUNDO = 0x0040; // 关键：允许撤销(即放入回收站)
        private const ushort FOF_NOCONFIRMATION = 0x0010; // 不显示系统确认框(UI层自己处理)
        private const ushort FOF_NOERRORUI = 0x0400;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

        /// <summary>
        /// 安全删除：将文件移动到回收站
        /// </summary>
        public async Task<bool> MoveToRecycleBinAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var shf = new SHFILEOPSTRUCT
                    {
                        wFunc = FO_DELETE,
                        pFrom = filePath + "\0\0", // 必须以双 null 结尾
                        fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI
                    };
                    return SHFileOperation(ref shf) == 0; // 返回 0 表示成功
                }
                catch { return false; }
            });
        }
    }
}
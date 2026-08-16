using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueSapphire.Services.Cleaner
{
    public sealed class CleanerDriveService
    {
        public IReadOnlyList<CleanerDriveOption> GetAvailableDrives()
        {
            string systemDriveRoot = NormalizePath(Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty);

            List<CleanerDriveOption> drives = new();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable))
                    {
                        continue;
                    }

                    string rootPath = NormalizePath(drive.RootDirectory.FullName);
                    drives.Add(new CleanerDriveOption
                    {
                        RootPath = rootPath,
                        Name = drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        VolumeLabel = SafeRead(() => drive.VolumeLabel),
                        FileSystem = SafeRead(() => drive.DriveFormat),
                        TotalBytes = SafeReadLong(() => drive.TotalSize),
                        FreeBytes = SafeReadLong(() => drive.AvailableFreeSpace),
                        IsSystemDrive = string.Equals(rootPath, systemDriveRoot, StringComparison.OrdinalIgnoreCase),
                        DriveKindText = drive.DriveType == DriveType.Removable ? "可移动磁盘" : "本地磁盘"
                    });
                }
                catch
                {
                }
            }

            return drives
                .OrderByDescending(drive => drive.IsSystemDrive)
                .ThenBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string SafeRead(Func<string> accessor)
        {
            try
            {
                return accessor() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static long SafeReadLong(Func<long> accessor)
        {
            try
            {
                return accessor();
            }
            catch
            {
                return 0;
            }
        }

        private static string NormalizePath(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string rootPath = Path.GetPathRoot(fullPath) ?? string.Empty;

                // 驱动器根目录必须保留尾部分隔符。把 C:\ 变成 C: 后，
                // Path.GetFullPath("C:") 会按当前工作目录解析，深度扫描就会
                // 错把应用目录当成整块磁盘。
                if (string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return rootPath;
                }

                return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }
}

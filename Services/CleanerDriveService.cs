using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueSapphire.Services
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
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }
}

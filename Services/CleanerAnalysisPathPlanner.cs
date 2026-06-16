using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueSapphire.Services
{
    public static class CleanerAnalysisPathPlanner
    {
        private static readonly HashSet<string> SkippedDriveLevelDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin",
            "System Volume Information"
        };

        public static IReadOnlyList<string> BuildAnalysisRoots(IEnumerable<string> selectedDriveRoots)
        {
            string systemDriveRoot = CleanerPathSafety.NormalizePath(Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty);
            string localAppData = CleanerPathSafety.NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            string roaming = CleanerPathSafety.NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            string downloads = CleanerPathSafety.NormalizePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

            HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
            foreach (string driveRoot in selectedDriveRoots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Select(CleanerPathSafety.NormalizePath))
            {
                if (string.Equals(driveRoot, systemDriveRoot, StringComparison.OrdinalIgnoreCase))
                {
                    roots.Add(localAppData);
                    roots.Add(roaming);
                    roots.Add(downloads);
                }
                else
                {
                    roots.Add(driveRoot);
                }
            }

            if (roots.Count == 0)
            {
                roots.Add(localAppData);
                roots.Add(roaming);
                roots.Add(downloads);
            }

            return roots.ToList();
        }

        public static IReadOnlyList<string> EnumerateCandidates(string root)
        {
            if (!Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            try
            {
                if (IsDriveRoot(root))
                {
                    return CleanerPathSafety.SafeEnumerateDirectories(root)
                        .Where(path => !ShouldSkipDriveLevelDirectory(path))
                        .ToList();
                }

                return CleanerPathSafety.SafeEnumerateDirectories(root).ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static bool ShouldSkipDriveLevelDirectory(string path)
        {
            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return SkippedDriveLevelDirectories.Contains(name);
        }

        private static bool IsDriveRoot(string path)
        {
            string normalized = CleanerPathSafety.NormalizePath(path);
            string root = CleanerPathSafety.NormalizePath(Path.GetPathRoot(normalized) ?? string.Empty);
            return string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueSapphire.Services
{
    internal static class CleanerPathSafety
    {
        private const int SharingViolation = 32;
        private const int LockViolation = 33;

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            try
            {
                string fullPath = Path.GetFullPath(path);
                string root = Path.GetPathRoot(fullPath) ?? string.Empty;
                
                if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath.EndsWith(Path.DirectorySeparatorChar) || fullPath.EndsWith(Path.AltDirectorySeparatorChar)
                        ? fullPath
                        : fullPath + Path.DirectorySeparatorChar;
                }

                return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        public static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsExcluded(string path, IEnumerable<string> exclusions)
        {
            string normalizedPath = NormalizePath(path);
            foreach (string exclusion in exclusions)
            {
                if (string.IsNullOrWhiteSpace(exclusion))
                {
                    continue;
                }

                if (StartsWithPathBoundary(normalizedPath, exclusion))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool StartsWithPathBoundary(string path, string prefix)
        {
            string normalizedPath = NormalizePath(path);
            string normalizedPrefix = NormalizePath(prefix);

            if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedPrefix))
            {
                return false;
            }

            if (string.Equals(normalizedPath, normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!normalizedPath.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Length <= normalizedPrefix.Length)
            {
                return false;
            }

            char separator = normalizedPath[normalizedPrefix.Length];
            return separator == Path.DirectorySeparatorChar || separator == Path.AltDirectorySeparatorChar;
        }

        public static IReadOnlyList<string> SafeEnumerateDirectories(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static IReadOnlyList<string> SafeEnumerateFiles(string path, string pattern = "*")
        {
            try
            {
                return Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static IReadOnlyList<string> EnumerateFilesSafely(
            string root,
            IReadOnlyList<string> patterns,
            bool recursive,
            IEnumerable<string> exclusions)
        {
            string normalizedRoot = NormalizePath(root);
            if (!Directory.Exists(normalizedRoot) || IsReparsePoint(normalizedRoot))
            {
                return Array.Empty<string>();
            }

            IReadOnlyList<string> effectivePatterns = patterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (effectivePatterns.Count == 0)
            {
                effectivePatterns = ["*"];
            }

            List<string> files = new();
            Stack<string> pending = new();
            pending.Push(normalizedRoot);

            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (IsExcluded(current, exclusions))
                {
                    continue;
                }

                foreach (string pattern in effectivePatterns)
                {
                    foreach (string file in SafeEnumerateFiles(current, pattern))
                    {
                        string normalizedFile = NormalizePath(file);
                        if (!IsExcluded(normalizedFile, exclusions))
                        {
                            files.Add(normalizedFile);
                        }
                    }
                }

                if (!recursive)
                {
                    continue;
                }

                foreach (string directory in SafeEnumerateDirectories(current))
                {
                    string normalizedDirectory = NormalizePath(directory);
                    if (IsExcluded(normalizedDirectory, exclusions) || IsReparsePoint(normalizedDirectory))
                    {
                        continue;
                    }

                    pending.Push(normalizedDirectory);
                }
            }

            return files;
        }

        public static bool IsFileLocked(string path)
        {
            try
            {
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return false;
            }
            catch (IOException ex) when (IsLockConflict(ex))
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsLockConflict(IOException exception)
        {
            int code = exception.HResult & 0xFFFF;
            return code == SharingViolation || code == LockViolation;
        }
    }
}

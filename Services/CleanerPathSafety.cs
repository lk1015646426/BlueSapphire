using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

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

            if (normalizedPrefix.EndsWith(Path.DirectorySeparatorChar) ||
                normalizedPrefix.EndsWith(Path.AltDirectorySeparatorChar))
            {
                return normalizedPath.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase);
            }

            if (!normalizedPath.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Length <= normalizedPrefix.Length)
            {
                return false;
            }

            char separator = normalizedPath[normalizedPrefix.Length];
            return separator == Path.DirectorySeparatorChar || separator == Path.AltDirectorySeparatorChar;
        }

        public static IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            IEnumerator<string>? enumerator = null;
            try
            {
                enumerator = Directory.EnumerateDirectories(path).GetEnumerator();
            }
            catch
            {
                yield break;
            }

            using (enumerator)
            {
                while (true)
                {
                    string item;
                    try
                    {
                        if (!enumerator.MoveNext()) break;
                        item = enumerator.Current;
                    }
                    catch
                    {
                        break;
                    }
                    yield return item;
                }
            }
        }

        public static IEnumerable<string> SafeEnumerateFiles(string path, string pattern = "*")
        {
            IEnumerator<string>? enumerator = null;
            try
            {
                enumerator = Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly).GetEnumerator();
            }
            catch
            {
                yield break;
            }

            using (enumerator)
            {
                while (true)
                {
                    string item;
                    try
                    {
                        if (!enumerator.MoveNext()) break;
                        item = enumerator.Current;
                    }
                    catch
                    {
                        break;
                    }
                    yield return item;
                }
            }
        }

        public static IEnumerable<string> EnumerateFilesSafely(
            string root,
            IReadOnlyList<string> patterns,
            bool recursive,
            IEnumerable<string> exclusions,
            CancellationToken cancellationToken = default)
        {
            string normalizedRoot = NormalizePath(root);
            if (!Directory.Exists(normalizedRoot) || IsReparsePoint(normalizedRoot))
            {
                yield break;
            }

            IReadOnlyList<string> effectivePatterns = patterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (effectivePatterns.Count == 0)
            {
                effectivePatterns = ["*"];
            }

            Stack<string> pending = new();
            pending.Push(normalizedRoot);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string current = pending.Pop();
                if (IsExcluded(current, exclusions))
                {
                    continue;
                }

                foreach (string pattern in effectivePatterns)
                {
                    foreach (string file in SafeEnumerateFiles(current, pattern))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string normalizedFile = NormalizePath(file);
                        if (!IsExcluded(normalizedFile, exclusions))
                        {
                            yield return normalizedFile;
                        }
                    }
                }

                if (!recursive)
                {
                    continue;
                }

                foreach (string directory in SafeEnumerateDirectories(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string normalizedDirectory = NormalizePath(directory);
                    if (IsExcluded(normalizedDirectory, exclusions) || IsReparsePoint(normalizedDirectory))
                    {
                        continue;
                    }

                    pending.Push(normalizedDirectory);
                }
            }
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

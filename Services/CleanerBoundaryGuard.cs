using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueSapphire.Services
{
    public sealed class CleanerBoundaryGuard
    {
        public CleanerBoundaryValidationResult Validate(CleanerScanItem item, string targetPath, bool isElevated)
        {
            string normalizedTarget = NormalizePath(targetPath);
            if (IsBroadProtectedRoot(normalizedTarget))
            {
                return CleanerBoundaryValidationResult.Fail(
                    CleanerFailureReason.BoundaryBlocked,
                    "目标为系统或主目录核心根路径，已阻止执行。");
            }

            if (item.RequiresElevation && !isElevated)
            {
                return CleanerBoundaryValidationResult.Fail(
                    CleanerFailureReason.ElevationRequired,
                    "当前对象属于系统级目录，需要管理员模式。");
            }

            List<string> boundaryRoots = item.BoundaryRoots
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (boundaryRoots.Count > 0)
            {
                if (boundaryRoots.Any(IsBroadProtectedRoot))
                {
                    return CleanerBoundaryValidationResult.Fail(
                        CleanerFailureReason.BoundaryBlocked,
                        "规则边界过宽，已阻止执行。");
                }

                bool withinBoundary = boundaryRoots.Any(root => IsPathInsideRoot(normalizedTarget, root));
                if (!withinBoundary)
                {
                    return CleanerBoundaryValidationResult.Fail(
                        CleanerFailureReason.BoundaryBlocked,
                        "目标超出规则声明的清理边界。");
                }

                string matchedBoundary = boundaryRoots.First(root => IsPathInsideRoot(normalizedTarget, root));
                if (ContainsReparsePointBetween(normalizedTarget, matchedBoundary))
                {
                    return CleanerBoundaryValidationResult.Fail(
                        CleanerFailureReason.ReparsePointSkipped,
                        "目标路径或其父目录包含符号链接/Junction，无法证明真实路径仍在规则边界内。");
                }
            }
            else if (item.RequiresElevation)
            {
                return CleanerBoundaryValidationResult.Fail(
                    CleanerFailureReason.BoundaryBlocked,
                    "系统级规则未声明允许清理的边界根目录。");
            }
            else
            {
                string windows = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                string progFiles = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
                string progFilesX86 = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

                if (IsPathInsideRoot(normalizedTarget, windows) ||
                    IsPathInsideRoot(normalizedTarget, progFiles) ||
                    IsPathInsideRoot(normalizedTarget, progFilesX86))
                {
                    return CleanerBoundaryValidationResult.Fail(
                        CleanerFailureReason.ElevationRequired,
                        "清理系统目录下的目标必须声明为提权规则。");
                }

                string? driveRoot = Path.GetPathRoot(normalizedTarget);
                if (!string.IsNullOrWhiteSpace(driveRoot) &&
                    ContainsReparsePointBetween(normalizedTarget, driveRoot))
                {
                    return CleanerBoundaryValidationResult.Fail(
                        CleanerFailureReason.ReparsePointSkipped,
                        "目标路径或其父目录包含符号链接/Junction，已阻止跨目录清理。");
                }
            }

            return CleanerBoundaryValidationResult.Success();
        }

        private static bool IsPathInsideRoot(string candidate, string root)
        {
            if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsReparsePointBetween(string target, string boundaryRoot)
        {
            string normalizedBoundary = NormalizePath(boundaryRoot);
            string? current = File.Exists(target)
                ? Path.GetDirectoryName(target)
                : target;

            while (!string.IsNullOrWhiteSpace(current) && IsPathInsideRoot(current, normalizedBoundary))
            {
                if (CleanerPathSafety.IsReparsePoint(current))
                {
                    return true;
                }

                if (string.Equals(NormalizePath(current), normalizedBoundary, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                DirectoryInfo? parent = Directory.GetParent(current);
                if (parent == null || string.Equals(parent.FullName, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = parent.FullName;
            }

            return CleanerPathSafety.IsReparsePoint(target);
        }

        private static readonly Lazy<string[]> _cachedBroadRoots = new Lazy<string[]>(() =>
        {
            string windows = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string programFiles = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            string programFilesX86 = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            string programData = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            string userProfile = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            return new[]
            {
                windows,
                programFiles,
                programFilesX86,
                programData,
                userProfile
            };
        });

        private static bool IsBroadProtectedRoot(string path)
        {
            string driveRoot = Path.GetPathRoot(path) ?? string.Empty;

            string[] exactRoots = _cachedBroadRoots.Value;

            if (!string.IsNullOrWhiteSpace(driveRoot) && string.Equals(path, driveRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return exactRoots.Any(root =>
                !string.IsNullOrWhiteSpace(root) &&
                string.Equals(path, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
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

    public sealed class CleanerBoundaryValidationResult
    {
        public bool IsAllowed { get; init; }
        public CleanerFailureReason FailureReason { get; init; } = CleanerFailureReason.None;
        public string Message { get; init; } = string.Empty;

        public static CleanerBoundaryValidationResult Success()
        {
            return new CleanerBoundaryValidationResult { IsAllowed = true };
        }

        public static CleanerBoundaryValidationResult Fail(CleanerFailureReason failureReason, string message)
        {
            return new CleanerBoundaryValidationResult
            {
                IsAllowed = false,
                FailureReason = failureReason,
                Message = message
            };
        }
    }
}

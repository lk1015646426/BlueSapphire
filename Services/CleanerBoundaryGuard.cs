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
            if (item.RequiresElevation && !isElevated)
            {
                return CleanerBoundaryValidationResult.Fail(
                    CleanerFailureReason.ElevationRequired,
                    "当前对象属于系统级目录，需要管理员模式。");
            }

            if (!item.RequiresElevation)
            {
                return CleanerBoundaryValidationResult.Success();
            }

            List<string> boundaryRoots = item.BoundaryRoots
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (boundaryRoots.Count == 0)
            {
                return CleanerBoundaryValidationResult.Fail(
                    CleanerFailureReason.BoundaryBlocked,
                    "系统级规则未声明允许清理的边界根目录。");
            }

            if (boundaryRoots.Any(IsBroadProtectedRoot))
            {
                return CleanerBoundaryValidationResult.Fail(
                    CleanerFailureReason.BoundaryBlocked,
                    "规则边界过宽，已阻止执行。");
            }

            string normalizedTarget = NormalizePath(targetPath);
            bool withinBoundary = boundaryRoots.Any(root => IsPathInsideRoot(normalizedTarget, root));
            if (!withinBoundary)
            {
                return CleanerBoundaryValidationResult.Fail(
                    CleanerFailureReason.BoundaryBlocked,
                    "目标超出规则声明的系统级清理边界。");
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

        private static bool IsBroadProtectedRoot(string path)
        {
            string windows = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string programFiles = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            string programFilesX86 = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            string programData = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            string userProfile = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            string[] blockedExactRoots =
            {
                Path.GetPathRoot(path) ?? string.Empty,
                windows,
                programFiles,
                programFilesX86,
                programData,
                userProfile
            };

            return blockedExactRoots.Any(root =>
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

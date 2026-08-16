using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BlueSapphire.Services.AI
{
    public sealed class AIOperationPolicyService
    {
        private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(2);
        private readonly ConcurrentDictionary<string, ConfirmationGrant> _grants =
            new(StringComparer.Ordinal);

        public IReadOnlyList<string> ValidateDriveRoots(IEnumerable<string> requestedRoots)
        {
            Dictionary<string, string> readyRoots = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .ToDictionary(
                    drive => NormalizeRoot(drive.RootDirectory.FullName),
                    drive => drive.RootDirectory.FullName,
                    StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            foreach (string requested in requestedRoots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string requestedFullPath = Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(requested.Trim().Trim('"')))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string normalized = NormalizeRoot(requested);
                if (!string.Equals(requestedFullPath, normalized, StringComparison.OrdinalIgnoreCase) ||
                    !readyRoots.TryGetValue(normalized, out string? canonicalRoot))
                {
                    throw new InvalidOperationException($"扫描范围不是当前可用磁盘根目录：{requested}");
                }
                result.Add(canonicalRoot);
            }
            return result;
        }

        public async Task<bool> ConfirmAsync(
            Func<string, Task<bool>>? requestConfirmation,
            string action,
            string fingerprint,
            string message)
        {
            if (requestConfirmation == null)
            {
                return false;
            }

            DateTimeOffset requestedAt = DateTimeOffset.Now;
            bool approved = await requestConfirmation(message);
            if (!approved || DateTimeOffset.Now - requestedAt > ConfirmationLifetime)
            {
                return false;
            }

            string grantId = Guid.NewGuid().ToString("N");
            _grants[grantId] = new ConfirmationGrant(
                action,
                fingerprint,
                DateTimeOffset.Now.Add(ConfirmationLifetime));
            return Consume(grantId, action, fingerprint);
        }

        private bool Consume(string grantId, string action, string fingerprint)
        {
            if (!_grants.TryRemove(grantId, out ConfirmationGrant? grant))
            {
                return false;
            }
            return grant.ExpiresAt >= DateTimeOffset.Now &&
                   string.Equals(grant.Action, action, StringComparison.Ordinal) &&
                   string.Equals(grant.Fingerprint, fingerprint, StringComparison.Ordinal);
        }

        private static string NormalizeRoot(string path)
        {
            string fullPath = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private sealed record ConfirmationGrant(
            string Action,
            string Fingerprint,
            DateTimeOffset ExpiresAt);
    }
}

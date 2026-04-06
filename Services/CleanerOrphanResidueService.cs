using BlueSapphire.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerOrphanResidueService
    {
        private const int MaxOwnerDepth = 2;
        private const int MaxCacheDepth = 3;
        private const int MaxVisitedDirectories = 512;
        private const int MaxVisitedFiles = 20000;

        private static readonly HashSet<string> CacheDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "cache",
            "cache_data",
            "code cache",
            "gpucache",
            "logs",
            "log",
            "temp",
            "tmp",
            "crashpad",
            "shadercache",
            "webcache",
            "cache2"
        };

        private static readonly HashSet<string> IgnoredRootNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Packages",
            "Package Cache",
            "Microsoft",
            "Temp",
            "CrashDumps",
            "BlueSapphire"
        };

        public CleanerOrphanResidueService(CleanerLockService lockService)
        {
            _ = lockService;
        }

        public async Task<List<CleanerScanItem>> ScanAsync(HashSet<string> exclusions, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                HashSet<string> installedAliases = LoadInstalledAppAliasesSafely();
                if (installedAliases.Count == 0)
                {
                    return new List<CleanerScanItem>();
                }

                List<string> roots = new()
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                };

                return AnalyzeOrphanedCacheDirectories(
                    roots.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)),
                    installedAliases,
                    exclusions,
                    minimumSizeBytes: 48L * 1024L * 1024L,
                    minimumFileCount: 150,
                    minimumAge: TimeSpan.FromDays(21),
                    cancellationToken);
            }, cancellationToken);
        }

        public List<CleanerScanItem> AnalyzeOrphanedCacheDirectories(
            IEnumerable<string> roots,
            ISet<string> installedAliases,
            HashSet<string> exclusions,
            long minimumSizeBytes,
            int minimumFileCount,
            TimeSpan minimumAge,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            HashSet<string> seenOwners = new(StringComparer.OrdinalIgnoreCase);
            List<CleanerScanItem> results = new();

            foreach (string root in roots
                .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                .Select(CleanerPathSafety.NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (string ownerRoot in EnumerateOwnerCandidates(root, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string normalizedOwner = CleanerPathSafety.NormalizePath(ownerRoot);
                    if (!seenOwners.Add(normalizedOwner))
                    {
                        continue;
                    }

                    if (CleanerPathSafety.IsExcluded(normalizedOwner, exclusions) ||
                        IsIgnoredOwner(normalizedOwner) ||
                        IsInstalledOwner(normalizedOwner, installedAliases))
                    {
                        continue;
                    }

                    if (!TryAnalyzeOwnerResidue(
                        normalizedOwner,
                        exclusions,
                        minimumSizeBytes,
                        minimumFileCount,
                        minimumAge,
                        now,
                        cancellationToken,
                        out ResidueAggregate? aggregate))
                    {
                        continue;
                    }

                    if (aggregate != null)
                    {
                        results.Add(BuildScanItem(aggregate));
                    }
                }
            }

            return results
                .OrderByDescending(item => item.SizeBytes)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> EnumerateOwnerCandidates(string root, CancellationToken cancellationToken)
        {
            Queue<(string Path, int Depth)> queue = new();
            queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                (string currentPath, int depth) = queue.Dequeue();
                if (depth >= MaxOwnerDepth)
                {
                    continue;
                }

                foreach (string child in CleanerPathSafety.SafeEnumerateDirectories(currentPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (CleanerPathSafety.IsReparsePoint(child))
                    {
                        continue;
                    }

                    string name = Path.GetFileName(child);
                    if (IgnoredRootNames.Contains(name))
                    {
                        continue;
                    }

                    yield return child;
                    queue.Enqueue((child, depth + 1));
                }
            }
        }

        private static bool TryAnalyzeOwnerResidue(
            string ownerRoot,
            HashSet<string> exclusions,
            long minimumSizeBytes,
            int minimumFileCount,
            TimeSpan minimumAge,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            out ResidueAggregate? aggregate)
        {
            aggregate = null;

            List<string> cacheDirectories = FindCacheDirectories(ownerRoot, exclusions, cancellationToken)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cacheDirectories.Count == 0)
            {
                return false;
            }

            long totalBytes = 0;
            int totalFiles = 0;
            DateTimeOffset lastModified = DateTimeOffset.MinValue;
            List<string> targetPaths = new();

            foreach (string cacheDirectory in cacheDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ResidueDirectoryStats stats = AggregateDirectorySafely(cacheDirectory, exclusions, cancellationToken);
                if (stats.FileCount == 0)
                {
                    continue;
                }

                totalBytes += stats.SizeBytes;
                totalFiles += stats.FileCount;
                if (lastModified < stats.LastModified)
                {
                    lastModified = stats.LastModified;
                }

                targetPaths.Add(cacheDirectory);
            }

            if (targetPaths.Count == 0 ||
                totalBytes < minimumSizeBytes ||
                totalFiles < minimumFileCount ||
                (lastModified != DateTimeOffset.MinValue && now - lastModified < minimumAge))
            {
                return false;
            }

            aggregate = new ResidueAggregate
            {
                OwnerRoot = ownerRoot,
                DisplayName = BuildOwnerDisplayName(ownerRoot),
                SizeBytes = totalBytes,
                FileCount = totalFiles,
                LastModified = lastModified == DateTimeOffset.MinValue
                    ? SafeGetLastWriteTime(ownerRoot)
                    : lastModified,
                TargetPaths = targetPaths
            };

            return true;
        }

        private static IEnumerable<string> FindCacheDirectories(
            string ownerRoot,
            HashSet<string> exclusions,
            CancellationToken cancellationToken)
        {
            Queue<(string Path, int Depth)> queue = new();
            queue.Enqueue((ownerRoot, 0));

            while (queue.Count > 0)
            {
                (string currentPath, int depth) = queue.Dequeue();
                if (depth > MaxCacheDepth)
                {
                    continue;
                }

                foreach (string child in CleanerPathSafety.SafeEnumerateDirectories(currentPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string normalizedChild = CleanerPathSafety.NormalizePath(child);
                    if (CleanerPathSafety.IsExcluded(normalizedChild, exclusions) || CleanerPathSafety.IsReparsePoint(normalizedChild))
                    {
                        continue;
                    }

                    string name = Path.GetFileName(normalizedChild);
                    if (CacheDirectoryNames.Contains(name))
                    {
                        yield return normalizedChild;
                        continue;
                    }

                    if (depth < MaxCacheDepth)
                    {
                        queue.Enqueue((normalizedChild, depth + 1));
                    }
                }
            }
        }

        private static ResidueDirectoryStats AggregateDirectorySafely(
            string root,
            HashSet<string> exclusions,
            CancellationToken cancellationToken)
        {
            long sizeBytes = 0;
            int fileCount = 0;
            int visitedDirectories = 0;
            int visitedFiles = 0;
            DateTimeOffset lastModified = DateTimeOffset.MinValue;
            Stack<string> pending = new();
            pending.Push(root);

            while (pending.Count > 0 && visitedDirectories < MaxVisitedDirectories && visitedFiles < MaxVisitedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string current = pending.Pop();
                visitedDirectories++;
                if (CleanerPathSafety.IsExcluded(current, exclusions) || CleanerPathSafety.IsReparsePoint(current))
                {
                    continue;
                }

                foreach (string file in CleanerPathSafety.SafeEnumerateFiles(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (visitedFiles >= MaxVisitedFiles || CleanerPathSafety.IsExcluded(file, exclusions))
                    {
                        break;
                    }

                    try
                    {
                        FileInfo info = new(file);
                        sizeBytes += info.Length;
                        fileCount++;
                        visitedFiles++;

                        DateTimeOffset fileWriteTime = info.LastWriteTimeUtc;
                        if (lastModified < fileWriteTime)
                        {
                            lastModified = fileWriteTime;
                        }
                    }
                    catch
                    {
                    }
                }

                foreach (string directory in CleanerPathSafety.SafeEnumerateDirectories(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (visitedDirectories >= MaxVisitedDirectories)
                    {
                        break;
                    }

                    if (!CleanerPathSafety.IsReparsePoint(directory))
                    {
                        pending.Push(directory);
                    }
                }
            }

            if (lastModified == DateTimeOffset.MinValue)
            {
                lastModified = SafeGetLastWriteTime(root);
            }

            return new ResidueDirectoryStats
            {
                SizeBytes = sizeBytes,
                FileCount = fileCount,
                LastModified = lastModified
            };
        }

        private static CleanerScanItem BuildScanItem(ResidueAggregate aggregate)
        {
            return new CleanerScanItem
            {
                RuleId = "orphan_leftover_analysis",
                Name = $"{aggregate.DisplayName} 疑似卸载残留",
                Description = "未在当前已安装软件清单中稳定匹配到对应应用，但其缓存/日志目录仍在占用空间。",
                Category = "orphan_leftover",
                Path = aggregate.OwnerRoot,
                TargetPaths = aggregate.TargetPaths,
                SizeBytes = aggregate.SizeBytes,
                FileCount = aggregate.FileCount,
                ModifyTime = aggregate.LastModified,
                OwnerApp = aggregate.DisplayName,
                RiskLevel = CleanerRiskLevel.High,
                CleanScore = 25,
                ExecutionMode = CleanerExecutionMode.None,
                ScanKind = CleanerScanKind.Directory,
                IsLocked = false,
                LockedByProcesses = new List<string>(),
                ViewOnly = true,
                WhyItConsumesSpace = "目录中仍保留缓存、日志或临时文件，但当前系统未稳定识别到对应已安装应用。",
                WhyItCanBeCleaned = "当前只把它当成提示项展示，不直接纳入执行清理。",
                ImpactAfterCleanup = "若该应用实际上仍在使用便携版、自定义安装版或 Store 版本，直接删除可能影响运行。",
                RegenerationHint = "建议先打开目录确认应用来源，确认确实已卸载后再手动处理。",
                RiskSummary = "高风险：疑似卸载残留暂时仅供查看，不参与一键清理",
                RiskDetail = "当前识别仍以保守提示为主，不直接执行删除，避免把便携版或自定义安装应用误判成残留",
                CanSelect = false,
                IsSelected = false,
                IsExcluded = false
            };
        }

        private static bool IsInstalledOwner(string ownerRoot, ISet<string> installedAliases)
        {
            if (installedAliases.Count == 0)
            {
                return false;
            }

            List<string> tokens = ownerRoot
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Select(NormalizeAlias)
                .Where(token => token.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return tokens.Any(installedAliases.Contains);
        }

        private static bool IsIgnoredOwner(string ownerRoot)
        {
            string[] segments = ownerRoot
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToArray();

            return segments
                .TakeLast(3)
                .Any(segment => IgnoredRootNames.Contains(segment));
        }

        private static string BuildOwnerDisplayName(string ownerRoot)
        {
            string[] segments = ownerRoot
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToArray();

            if (segments.Length >= 2)
            {
                return string.Join(" / ", segments[^2], segments[^1]);
            }

            return Path.GetFileName(ownerRoot);
        }

        private static HashSet<string> LoadInstalledAppAliasesSafely()
        {
            HashSet<string> aliases = new(StringComparer.OrdinalIgnoreCase);

            foreach (RegistryKey? root in OpenUninstallRoots())
            {
                if (root == null)
                {
                    continue;
                }

                try
                {
                    foreach (string subKeyName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using RegistryKey? appKey = root.OpenSubKey(subKeyName);
                            if (appKey == null)
                            {
                                continue;
                            }

                            AddAlias(aliases, appKey.GetValue("DisplayName") as string);
                            AddAlias(aliases, appKey.GetValue("Publisher") as string);

                            string? installLocation = appKey.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrWhiteSpace(installLocation))
                            {
                                AddAlias(aliases, Path.GetFileName(installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    root.Dispose();
                }
            }

            return aliases;
        }

        private static IEnumerable<RegistryKey?> OpenUninstallRoots()
        {
            RegistryKey?[] roots =
            [
                TryOpenRegistryKey(() => Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")),
                TryOpenRegistryKey(() => Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")),
                TryOpenRegistryKey(() => Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
            ];

            foreach (RegistryKey? root in roots)
            {
                if (root != null)
                {
                    yield return root;
                }
            }
        }

        private static RegistryKey? TryOpenRegistryKey(Func<RegistryKey?> factory)
        {
            try
            {
                return factory();
            }
            catch
            {
                return null;
            }
        }

        private static void AddAlias(HashSet<string> aliases, string? rawValue)
        {
            string normalized = NormalizeAlias(rawValue);
            if (normalized.Length >= 3)
            {
                aliases.Add(normalized);
            }

            foreach (string token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length >= 3)
                {
                    aliases.Add(token);
                }
            }
        }

        private static string NormalizeAlias(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return string.Empty;
            }

            return new string(rawValue
                .Trim()
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
                .ToArray())
                .Replace("  ", " ")
                .Trim();
        }

        private static DateTimeOffset SafeGetLastWriteTime(string path)
        {
            try
            {
                return Directory.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTimeOffset.MinValue;
            }
        }

        private sealed class ResidueDirectoryStats
        {
            public long SizeBytes { get; init; }
            public int FileCount { get; init; }
            public DateTimeOffset LastModified { get; init; }
        }

        private sealed class ResidueAggregate
        {
            public string OwnerRoot { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public long SizeBytes { get; init; }
            public int FileCount { get; init; }
            public DateTimeOffset LastModified { get; init; }
            public List<string> TargetPaths { get; init; } = new();
        }
    }
}

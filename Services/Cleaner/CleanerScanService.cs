using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Services.Cleaner
{
    public sealed class CleanerScanService
    {
        private static readonly TimeSpan IncrementalReuseWindow = TimeSpan.FromMinutes(5);
        // 目录扫描是 I/O 密集型工作。并发过高会造成磁盘争用，虽然工作在线程池中，
        // 仍可能让整个应用表现为卡顿，因此只允许少量规则同时枚举磁盘。
        private readonly SemaphoreSlim _scanThrottle = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));

        private readonly CleanerRuleService _ruleService;
        private readonly CleanerRiskEvaluator _riskEvaluator;
        private readonly CleanerStateStore _stateStore;
        private readonly CleanerLockService _lockService;
        private readonly CleanerPrivilegeService _privilegeService;
        private readonly ILogger<CleanerScanService>? _logger;
        private readonly CleanerApplicationDiscoveryService? _applicationDiscoveryService;
        private CachedQuickScanSegment? _cachedQuickScan;

        public CleanerScanService(
            CleanerRuleService ruleService,
            CleanerRiskEvaluator riskEvaluator,
            CleanerStateStore stateStore,
            CleanerLockService lockService,
            CleanerPrivilegeService privilegeService,
            ILogger<CleanerScanService>? logger = null,
            CleanerApplicationDiscoveryService? applicationDiscoveryService = null)
        {
            _ruleService = ruleService;
            _riskEvaluator = riskEvaluator;
            _stateStore = stateStore;
            _lockService = lockService;
            _privilegeService = privilegeService;
            _logger = logger;
            _applicationDiscoveryService = applicationDiscoveryService;
        }

        public async Task<CleanerScanReport> ScanAsync(
            CleanerScanScope scope,
            CleanerScanOptions? options,
            IProgress<CleanerScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            _logger?.LogInformation("[CleanerScanService] 开始执行扫描，作用域: {Scope}", scope);
            DateTimeOffset start = DateTimeOffset.Now;
            IReadOnlyList<CleanerRuleDefinition> rules = await _ruleService.GetRulesAsync().ConfigureAwait(false);
            IReadOnlyList<CleanerExclusionEntry> exclusions = await _stateStore.LoadExclusionsAsync().ConfigureAwait(false);
            CleanerScanOptions scanOptions = options ?? new CleanerScanOptions();
            HashSet<string> selectedDriveRoots = ResolveSelectedDriveRoots(scanOptions.AnalysisDriveRoots);
            HashSet<string> exclusionLookup = exclusions
                .Select(entry => CleanerPathSafety.NormalizePath(entry.Path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<CleanerScanItem> items = new();
            int reusedItemCount = 0;
            bool usedIncrementalReuse = false;
            List<CleanerRuleDefinition> quickRules = rules
                .Where(rule => rule.Scope == CleanerScanScope.Quick)
                .ToList();
            List<CleanerRuleDefinition> deepOnlyRules = scope == CleanerScanScope.Deep
                ? rules.Where(rule => rule.Scope == CleanerScanScope.Deep).ToList()
                : new List<CleanerRuleDefinition>();
            int progressMax = Math.Max(1, scope == CleanerScanScope.Quick ? quickRules.Count : quickRules.Count + deepOnlyRules.Count);
            int progressValue = 0;

            string quickFingerprint = scope == CleanerScanScope.Deep
                ? await BuildQuickFingerprintAsync(
                    quickRules,
                    exclusionLookup,
                    selectedDriveRoots,
                    _privilegeService.IsElevated,
                    cancellationToken).ConfigureAwait(false)
                : string.Empty;
            List<CleanerScanItem> scannedQuickItems = new();

            if (scope == CleanerScanScope.Deep && TryGetReusableQuickItems(quickFingerprint, out List<CleanerScanItem> cachedQuickItems))
            {
                progress?.Report(new CleanerScanProgress
                {
                    StageTitle = "增量扫描",
                    Detail = $"复用最近快速扫描结果：{cachedQuickItems.Count} 项",
                    ProgressValue = progressValue,
                    ProgressMax = progressMax
                });

                items.AddRange(cachedQuickItems);
                reusedItemCount = cachedQuickItems.Count;
                usedIncrementalReuse = reusedItemCount > 0;
                progressValue += quickRules.Count;
            }
            else
            {
                int localProgressValue = progressValue;

                var quickTasks = quickRules.Select(async rule =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _scanThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        progress?.Report(new CleanerScanProgress
                        {
                            StageTitle = scope == CleanerScanScope.Quick ? "快速扫描" : "深度扫描",
                            Detail = $"正在检查：{rule.Name}",
                            ProgressValue = Volatile.Read(ref localProgressValue),
                            ProgressMax = progressMax
                        });

                        List<CleanerScanItem> ruleItems = await ScanRuleAsync(
                            rule,
                            exclusionLookup,
                            selectedDriveRoots,
                            cancellationToken).ConfigureAwait(false);
                        int completedProgress = Interlocked.Increment(ref localProgressValue);
                        progress?.Report(new CleanerScanProgress
                        {
                            StageTitle = scope == CleanerScanScope.Quick ? "快速扫描" : "深度扫描",
                            Detail = $"已检查：{rule.Name}",
                            ProgressValue = completedProgress,
                            ProgressMax = progressMax
                        });
                        return ruleItems;
                    }
                    finally
                    {
                        _scanThrottle.Release();
                    }
                });

                var quickResults = await Task.WhenAll(quickTasks).ConfigureAwait(false);
                
                foreach (var ruleItems in quickResults)
                {
                    scannedQuickItems.AddRange(ruleItems);
                    items.AddRange(ruleItems);
                }

                progressValue = localProgressValue;

                progress?.Report(new CleanerScanProgress
                {
                    StageTitle = scope == CleanerScanScope.Quick ? "快速扫描" : "深度扫描",
                    Detail = "正在整理扫描结果，可随时取消",
                    ProgressValue = progressValue,
                    ProgressMax = progressMax
                });

                string completedQuickFingerprint = await BuildQuickFingerprintAsync(
                    quickRules,
                    exclusionLookup,
                    selectedDriveRoots,
                    _privilegeService.IsElevated,
                    cancellationToken).ConfigureAwait(false);
                StoreQuickCache(completedQuickFingerprint, scannedQuickItems);
            }

            if (scope == CleanerScanScope.Deep)
            {
                int localDeepProgressValue = progressValue;

                var deepTasks = deepOnlyRules.Select(async rule =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _scanThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        progress?.Report(new CleanerScanProgress
                        {
                            StageTitle = "深度扫描",
                            Detail = $"正在检查：{rule.Name}",
                            ProgressValue = Volatile.Read(ref localDeepProgressValue),
                            ProgressMax = progressMax
                        });

                        List<CleanerScanItem> ruleItems = await ScanRuleAsync(
                            rule,
                            exclusionLookup,
                            selectedDriveRoots,
                            cancellationToken).ConfigureAwait(false);
                        int completedProgress = Interlocked.Increment(ref localDeepProgressValue);
                        progress?.Report(new CleanerScanProgress
                        {
                            StageTitle = "深度扫描",
                            Detail = $"已检查：{rule.Name}",
                            ProgressValue = completedProgress,
                            ProgressMax = progressMax
                        });
                        return ruleItems;
                    }
                    finally
                    {
                        _scanThrottle.Release();
                    }
                });

                var deepResults = await Task.WhenAll(deepTasks).ConfigureAwait(false);
                foreach (var ruleItems in deepResults)
                {
                    items.AddRange(ruleItems);
                }
                progressValue = localDeepProgressValue;

            }

            DateTimeOffset completedAt = DateTimeOffset.Now;
            _logger?.LogInformation("[CleanerScanService] 扫描完成，作用域: {Scope}，耗时: {Duration}ms，共发现 {Count} 个清理项，复用缓存项 {ReusedCount} 个",
                scope, (completedAt - start).TotalMilliseconds, items.Count, reusedItemCount);

            return new CleanerScanReport
            {
                CreatedAt = completedAt,
                Scope = scope,
                Duration = completedAt - start,
                AnalysisDriveRoots = selectedDriveRoots
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                UsedIncrementalReuse = usedIncrementalReuse,
                ReusedItemCount = reusedItemCount,
                Items = items
                    .OrderByDescending(item => item.SizeBytes)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        public void InvalidateIncrementalCache()
        {
            _cachedQuickScan = null;
        }

        private Task<List<CleanerScanItem>> ScanRuleAsync(
            CleanerRuleDefinition rule,
            HashSet<string> exclusions,
            HashSet<string> selectedDriveRoots,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                List<CleanerScanItem> result = new();

            foreach (string rawPath in rule.Paths)
            {
                string resolvedRulePath = CleanerKnownPathResolver.Expand(rawPath ?? string.Empty);
                if (!IsPathOnSelectedDrive(resolvedRulePath, selectedDriveRoots))
                {
                    continue;
                }

                foreach (string expandedPath in ExpandPaths(rawPath ?? string.Empty, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(expandedPath))
                    {
                        continue;
                    }

                    string normalizedPath = CleanerPathSafety.NormalizePath(expandedPath);
                    if (!IsPathOnSelectedDrive(normalizedPath, selectedDriveRoots))
                    {
                        continue;
                    }

                    if (CleanerPathSafety.IsExcluded(normalizedPath, exclusions))
                    {
                        continue;
                    }

                    List<string> boundaryRoots = ExpandBoundaryRoots(rule, normalizedPath);
                    if (rule.RequiresElevation && !_privilegeService.IsElevated)
                    {
                        result.Add(BuildElevationRequiredPlaceholder(rule, normalizedPath, boundaryRoots));
                        continue;
                    }

                    ScanStats stats = CollectStats(rule, normalizedPath, exclusions, cancellationToken);

                    if (stats.SizeBytes <= 0 || stats.FileCount <= 0)
                    {
                        continue;
                    }

                    List<string> lockedBy = rule.PreflightLockCheck
                        ? _lockService.GetLockingProcesses(stats.LockProbePaths).ToList()
                        : new List<string>();
                    lockedBy.AddRange(GetRunningProcesses(rule.ProcessNames));
                    lockedBy = lockedBy.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    bool isLocked = stats.IsLocked || lockedBy.Count > 0;

                    CleanerRiskAssessment risk = _riskEvaluator.Evaluate(
                        rule,
                        normalizedPath,
                        isLocked,
                        stats.ModifyTime,
                        stats.SizeBytes);

                    bool viewOnly = rule.ViewOnly || risk.RiskLevel == CleanerRiskLevel.High || rule.ExecutionMode == CleanerExecutionMode.None;
                    bool canSelect = risk.CanSelect && !viewOnly;

                    result.Add(new CleanerScanItem
                    {
                        RuleId = rule.Id,
                        Name = rule.Name,
                        Description = rule.Description,
                        Category = rule.Category,
                        Path = normalizedPath,
                        SizeBytes = stats.SizeBytes,
                        FileCount = stats.FileCount,
                        ModifyTime = stats.ModifyTime,
                        OwnerApp = rule.OwnerApp,
                        SourceContextText = BuildSourceContext(rule, normalizedPath),
                        RiskLevel = risk.RiskLevel,
                        CleanScore = risk.Score,
                        ExecutionMode = rule.ExecutionMode,
                        SystemAction = rule.SystemAction,
                        ScanKind = rule.ScanKind,
                        IncludePatterns = rule.IncludePatterns,
                        IncludeSubdirectories = rule.IncludeSubdirectories,
                        MinAgeDays = rule.MinAgeDays,
                        MaxAgeDays = rule.MaxAgeDays,
                        TargetPaths = stats.TargetPaths,
                        IsLocked = isLocked,
                        DefaultSelected = rule.DefaultSelected,
                        RequiresElevation = rule.RequiresElevation,
                        IsElevatedMode = _privilegeService.IsElevated,
                        BoundaryRoots = boundaryRoots,
                        LockedByProcesses = lockedBy,
                        ViewOnly = viewOnly,
                        WhyItConsumesSpace = rule.WhyItConsumesSpace,
                        WhyItCanBeCleaned = rule.WhyItCanBeCleaned,
                        ImpactAfterCleanup = rule.ImpactAfterCleanup,
                        RegenerationHint = rule.RegenerationHint,
                        RiskSummary = risk.Summary,
                        RiskDetail = risk.Detail,
                        CanSelect = canSelect,
                        IsSelected = canSelect && rule.DefaultSelected,
                        IsExcluded = false
                    });
                }
            }
            return result;
            }, cancellationToken);
        }

        private CleanerScanItem BuildElevationRequiredPlaceholder(
            CleanerRuleDefinition rule,
            string normalizedPath,
            List<string> boundaryRoots)
        {
            return new CleanerScanItem
            {
                RuleId = rule.Id,
                Name = $"{rule.Name}（需要提权）",
                Description = $"{rule.Description} 当前标准模式下不扫描该系统级目录。",
                Category = rule.Category,
                Path = normalizedPath,
                OwnerApp = rule.OwnerApp,
                SourceContextText = BuildSourceContext(rule, normalizedPath),
                RiskLevel = CleanerRiskLevel.Medium,
                CleanScore = 40,
                ExecutionMode = rule.ExecutionMode,
                SystemAction = rule.SystemAction,
                ScanKind = rule.ScanKind,
                IncludePatterns = rule.IncludePatterns,
                IncludeSubdirectories = rule.IncludeSubdirectories,
                RequiresElevation = true,
                IsElevatedMode = false,
                BoundaryRoots = boundaryRoots,
                ViewOnly = true,
                WhyItConsumesSpace = rule.WhyItConsumesSpace,
                WhyItCanBeCleaned = rule.WhyItCanBeCleaned,
                ImpactAfterCleanup = rule.ImpactAfterCleanup,
                RegenerationHint = "先进入管理员模式，系统才会扫描并允许处理这类目录。",
                RiskSummary = "需要管理员模式才能扫描该系统级目录",
                RiskDetail = "为避免标准权限误判或越界访问，当前只做权限提示",
                DefaultSelected = false,
                CanSelect = false,
                IsSelected = false,
                IsExcluded = false
            };
        }

        private static List<string> ExpandBoundaryRoots(CleanerRuleDefinition rule, string normalizedPath)
        {
            IEnumerable<string> rawRoots = rule.BoundaryRoots.Count > 0 ? rule.BoundaryRoots : new[] { normalizedPath };
            return rawRoots
                .Select(CleanerKnownPathResolver.Expand)
                .Select(CleanerPathSafety.NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IReadOnlyList<string> GetRunningProcesses(IEnumerable<string> processNames)
        {
            List<string> running = new();
            foreach (string rawName in processNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string processName = Path.GetFileNameWithoutExtension(rawName.Trim());
                try
                {
                    Process[] processes = Process.GetProcessesByName(processName);
                    if (processes.Length > 0)
                    {
                        running.Add(processName);
                    }
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
                catch
                {
                }
            }

            return running;
        }

        private string BuildSourceContext(CleanerRuleDefinition rule, string path)
        {
            string normalized = path.Replace('/', '\\');
            string? profile = SegmentAfter(normalized, "\\User Data\\") ??
                              SegmentAfter(normalized, "\\Profiles\\");
            if (!string.IsNullOrWhiteSpace(profile))
            {
                return AppendInstalledContext($"{rule.OwnerApp} · 配置 {profile}", rule.OwnerApp);
            }

            if (string.Equals(rule.Id, "jetbrains_cache", StringComparison.OrdinalIgnoreCase))
            {
                string? version = SegmentAfter(normalized, "\\JetBrains\\");
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return AppendInstalledContext($"JetBrains · {version}", rule.OwnerApp);
                }
            }

            return AppendInstalledContext(rule.OwnerApp, rule.OwnerApp);
        }

        private string AppendInstalledContext(string pathContext, string ownerApp)
        {
            string installed = _applicationDiscoveryService?.GetInstalledContext(ownerApp) ?? string.Empty;
            return string.IsNullOrWhiteSpace(installed)
                ? pathContext
                : $"{pathContext} · {installed}";
        }

        private static string? SegmentAfter(string path, string marker)
        {
            int markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            string remainder = path[(markerIndex + marker.Length)..];
            int separatorIndex = remainder.IndexOf('\\');
            return separatorIndex < 0 ? remainder : remainder[..separatorIndex];
        }

        private IEnumerable<string> ExpandPaths(string rawPath, CancellationToken cancellationToken)
        {
            string expanded = CleanerKnownPathResolver.Expand(rawPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(expanded))
            {
                yield break;
            }

            if (!expanded.Contains('*') && !expanded.Contains('?'))
            {
                yield return expanded;
                yield break;
            }

            string root = Path.GetPathRoot(expanded) ?? string.Empty;
            string remainder = expanded[root.Length..];
            string[] segments = remainder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<string> current = new[] { root };

            foreach (string segment in segments)
            {
                bool wildcard = segment.Contains('*') || segment.Contains('?');
                List<string> next = new();

                foreach (string basePath in current)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!Directory.Exists(basePath))
                    {
                        continue;
                    }

                    if (!wildcard)
                    {
                        next.Add(Path.Combine(basePath, segment));
                        continue;
                    }

                    try
                    {
                        next.AddRange(CleanerPathSafety.SafeEnumerateDirectories(basePath)
                            .Where(candidate => IsPatternMatch(Path.GetFileName(candidate), segment)));
                            
                        // Breadth limit breaker
                        if (next.Count > 10000)
                        {
                            break;
                        }
                    }
                    catch
                    {
                    }
                }

                current = next.Take(10000);
            }

            foreach (string resolved in current.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return resolved;
            }
        }

        private static ScanStats CollectStats(
            CleanerRuleDefinition rule,
            string path,
            HashSet<string> exclusions,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rule.MinAgeDays.HasValue || rule.MaxAgeDays.HasValue)
            {
                return AggregateFilesByAge(rule, path, exclusions, cancellationToken);
            }

            return rule.ScanKind switch
            {
                CleanerScanKind.DirectoryContents => AggregateDirectory(path, recursive: true, exclusions, rule.PreflightLockCheck, cancellationToken, captureImmediateTargets: true),
                CleanerScanKind.FilesByPattern => AggregateFilesByPattern(path, rule.IncludePatterns, rule.IncludeSubdirectories, exclusions, rule.PreflightLockCheck, cancellationToken),
                CleanerScanKind.Directory => AggregateDirectory(path, recursive: true, exclusions, rule.PreflightLockCheck, cancellationToken),
                CleanerScanKind.File => AggregateFile(path, rule.PreflightLockCheck, cancellationToken),
                _ => new ScanStats()
            };
        }

        private static ScanStats AggregateDirectory(
            string path,
            bool recursive,
            HashSet<string> exclusions,
            bool collectLockProbes,
            CancellationToken cancellationToken,
            bool captureImmediateTargets = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(path) || CleanerPathSafety.IsReparsePoint(path))
            {
                return new ScanStats();
            }

            long sizeBytes = 0;
            int fileCount = 0;
            bool isLocked = false;
            DateTimeOffset latestWriteTime = DateTimeOffset.MinValue;
            List<string> lockProbePaths = new();
            List<string> targetPaths = new();
            string normalizedRoot = CleanerPathSafety.NormalizePath(path);
            Stack<string> pending = new();
            pending.Push(path);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = pending.Pop();
                if (CleanerPathSafety.IsExcluded(current, exclusions))
                {
                    continue;
                }

                try
                {
                    EnumerationOptions options = new EnumerationOptions { IgnoreInaccessible = true, ReturnSpecialDirectories = false };
                    DirectoryInfo dirInfo = new DirectoryInfo(current);
                    
                    foreach (FileInfo file in dirInfo.EnumerateFiles("*", options))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (CleanerPathSafety.IsExcluded(file.FullName, exclusions))
                        {
                            continue;
                        }

                        sizeBytes += file.Length;
                        fileCount++;
                        if (captureImmediateTargets && string.Equals(
                            CleanerPathSafety.NormalizePath(current),
                            normalizedRoot,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            targetPaths.Add(CleanerPathSafety.NormalizePath(file.FullName));
                        }
                        if (collectLockProbes && lockProbePaths.Count < 12)
                        {
                            lockProbePaths.Add(file.FullName);
                        }
                        if (latestWriteTime < file.LastWriteTimeUtc)
                        {
                            latestWriteTime = file.LastWriteTimeUtc;
                        }
                    }

                    if (!recursive)
                    {
                        continue;
                    }

                    foreach (DirectoryInfo subDir in dirInfo.EnumerateDirectories("*", options))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!CleanerPathSafety.IsReparsePoint(subDir.FullName))
                        {
                            if (captureImmediateTargets &&
                                string.Equals(
                                    CleanerPathSafety.NormalizePath(current),
                                    normalizedRoot,
                                    StringComparison.OrdinalIgnoreCase) &&
                                !CleanerPathSafety.IsExcluded(subDir.FullName, exclusions))
                            {
                                targetPaths.Add(CleanerPathSafety.NormalizePath(subDir.FullName));
                            }
                            pending.Push(subDir.FullName);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }

            return new ScanStats
            {
                SizeBytes = sizeBytes,
                FileCount = fileCount,
                ModifyTime = latestWriteTime == DateTimeOffset.MinValue ? Directory.GetLastWriteTimeUtc(path) : latestWriteTime,
                IsLocked = isLocked,
                LockProbePaths = lockProbePaths,
                TargetPaths = targetPaths
            };
        }

        private static ScanStats AggregateFilesByPattern(
            string path,
            IReadOnlyList<string> patterns,
            bool recursive,
            HashSet<string> exclusions,
            bool collectLockProbes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(path) || CleanerPathSafety.IsReparsePoint(path))
            {
                return new ScanStats();
            }

            long sizeBytes = 0;
            int fileCount = 0;
            DateTimeOffset latestWriteTime = DateTimeOffset.MinValue;
            bool isLocked = false;
            List<string> lockProbePaths = new();

            IEnumerable<string> files = CleanerPathSafety.EnumerateFilesSafely(
                path,
                patterns,
                recursive,
                exclusions,
                cancellationToken);
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    FileInfo info = new(file);
                    sizeBytes += info.Length;
                    fileCount++;
                    if (collectLockProbes && lockProbePaths.Count < 12)
                    {
                        lockProbePaths.Add(file);
                    }
                    if (latestWriteTime < info.LastWriteTimeUtc)
                    {
                        latestWriteTime = info.LastWriteTimeUtc;
                    }

                }
                catch
                {
                }
            }

            return new ScanStats
            {
                SizeBytes = sizeBytes,
                FileCount = fileCount,
                ModifyTime = latestWriteTime == DateTimeOffset.MinValue ? Directory.GetLastWriteTimeUtc(path) : latestWriteTime,
                IsLocked = isLocked,
                LockProbePaths = lockProbePaths
            };
        }

        private static ScanStats AggregateFile(string path, bool collectLockProbes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                return new ScanStats();
            }

            FileInfo info = new(path);
            return new ScanStats
            {
                SizeBytes = info.Length,
                FileCount = 1,
                ModifyTime = info.LastWriteTimeUtc,
                IsLocked = false,
                LockProbePaths = collectLockProbes ? new List<string> { path } : new List<string>()
            };
        }

        private static ScanStats AggregateFilesByAge(
            CleanerRuleDefinition rule,
            string path,
            HashSet<string> exclusions,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(path) || CleanerPathSafety.IsReparsePoint(path))
            {
                return rule.ScanKind == CleanerScanKind.File
                    ? AggregateAgedSingleFile(rule, path, cancellationToken)
                    : new ScanStats();
            }

            IReadOnlyList<string> patterns = rule.ScanKind == CleanerScanKind.FilesByPattern
                ? rule.IncludePatterns
                : Array.Empty<string>();
            bool recursive = rule.ScanKind != CleanerScanKind.FilesByPattern || rule.IncludeSubdirectories;
            IEnumerable<string> files = CleanerPathSafety.EnumerateFilesSafely(
                path,
                patterns,
                recursive,
                exclusions,
                cancellationToken);

            long sizeBytes = 0;
            int fileCount = 0;
            DateTimeOffset latestWriteTime = DateTimeOffset.MinValue;
            List<string> targets = new();
            List<string> lockProbePaths = new();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    FileInfo info = new(file);
                    if (!MatchesAgePolicy(info.LastWriteTimeUtc, now, rule.MinAgeDays, rule.MaxAgeDays))
                    {
                        continue;
                    }

                    sizeBytes += info.Length;
                    fileCount++;
                    targets.Add(file);
                    if (latestWriteTime < info.LastWriteTimeUtc)
                    {
                        latestWriteTime = info.LastWriteTimeUtc;
                    }
                    if (rule.PreflightLockCheck && lockProbePaths.Count < 12)
                    {
                        lockProbePaths.Add(file);
                    }
                }
                catch
                {
                }
            }

            return new ScanStats
            {
                SizeBytes = sizeBytes,
                FileCount = fileCount,
                ModifyTime = latestWriteTime,
                LockProbePaths = lockProbePaths,
                TargetPaths = targets
            };
        }

        private static ScanStats AggregateAgedSingleFile(
            CleanerRuleDefinition rule,
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                return new ScanStats();
            }

            FileInfo info = new(path);
            if (!MatchesAgePolicy(info.LastWriteTimeUtc, DateTimeOffset.UtcNow, rule.MinAgeDays, rule.MaxAgeDays))
            {
                return new ScanStats();
            }

            return new ScanStats
            {
                SizeBytes = info.Length,
                FileCount = 1,
                ModifyTime = info.LastWriteTimeUtc,
                LockProbePaths = rule.PreflightLockCheck ? new List<string> { path } : new List<string>(),
                TargetPaths = new List<string> { path }
            };
        }

        private static bool MatchesAgePolicy(
            DateTimeOffset lastWriteTime,
            DateTimeOffset now,
            int? minAgeDays,
            int? maxAgeDays)
        {
            double ageDays = Math.Max(0, (now - lastWriteTime.ToUniversalTime()).TotalDays);
            return (!minAgeDays.HasValue || ageDays >= minAgeDays.Value) &&
                   (!maxAgeDays.HasValue || ageDays < maxAgeDays.Value);
        }

        private bool TryGetReusableQuickItems(string fingerprint, out List<CleanerScanItem> items)
        {
            items = new List<CleanerScanItem>();
            if (_cachedQuickScan == null ||
                !string.Equals(_cachedQuickScan.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase) ||
                DateTimeOffset.Now - _cachedQuickScan.CapturedAt > IncrementalReuseWindow)
            {
                return false;
            }

            items = _cachedQuickScan.Items
                .Select(CloneScanItem)
                .ToList();

            return items.Count > 0;
        }

        private void StoreQuickCache(string fingerprint, IReadOnlyList<CleanerScanItem> items)
        {
            if (items.Count == 0)
            {
                _cachedQuickScan = null;
                return;
            }

            _cachedQuickScan = new CachedQuickScanSegment
            {
                Fingerprint = fingerprint,
                CapturedAt = DateTimeOffset.Now,
                Items = items
                    .Select(CloneScanItem)
                    .ToList()
            };
        }

        private static bool IsPatternMatch(string fileName, string pattern)
        {
            string escaped = Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            return Regex.IsMatch(fileName, $"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static CleanerScanItem CloneScanItem(CleanerScanItem source)
        {
            return new CleanerScanItem
            {
                ObjectId = source.ObjectId,
                RuleId = source.RuleId,
                Name = source.Name,
                Description = source.Description,
                Category = source.Category,
                Path = source.Path,
                SizeBytes = source.SizeBytes,
                FileCount = source.FileCount,
                ModifyTime = source.ModifyTime,
                OwnerApp = source.OwnerApp,
                SourceContextText = source.SourceContextText,
                RiskLevel = source.RiskLevel,
                CleanScore = source.CleanScore,
                ExecutionMode = source.ExecutionMode,
                SystemAction = source.SystemAction,
                ScanKind = source.ScanKind,
                IncludeSubdirectories = source.IncludeSubdirectories,
                MinAgeDays = source.MinAgeDays,
                MaxAgeDays = source.MaxAgeDays,
                IsLocked = source.IsLocked,
                ViewOnly = source.ViewOnly,
                CanSelect = source.CanSelect,
                DefaultSelected = source.DefaultSelected,
                RequiresElevation = source.RequiresElevation,
                IsElevatedMode = source.IsElevatedMode,
                IncludePatterns = source.IncludePatterns.ToList(),
                TargetPaths = source.TargetPaths.ToList(),
                BoundaryRoots = source.BoundaryRoots.ToList(),
                WhyItConsumesSpace = source.WhyItConsumesSpace,
                WhyItCanBeCleaned = source.WhyItCanBeCleaned,
                ImpactAfterCleanup = source.ImpactAfterCleanup,
                RegenerationHint = source.RegenerationHint,
                RiskSummary = source.RiskSummary,
                RiskDetail = source.RiskDetail,
                AIAnalysisText = source.AIAnalysisText,
                AIRecommendationText = source.AIRecommendationText,
                LockedByProcesses = source.LockedByProcesses.ToList(),
                IsSelected = source.IsSelected,
                IsExcluded = source.IsExcluded
            };
        }

        private static string BuildQuickFingerprint(
            IReadOnlyList<CleanerRuleDefinition> rules,
            HashSet<string> exclusions,
            HashSet<string> selectedDriveRoots,
            bool isElevated,
            CancellationToken cancellationToken)
        {
            HashCode hash = new();
            hash.Add(isElevated);
            int fingerprintedEntries = 0;
            const int maxFingerprintEntries = 100_000;

            foreach (string driveRoot in selectedDriveRoots.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                hash.Add(driveRoot, StringComparer.OrdinalIgnoreCase);
            }

            foreach (string exclusion in exclusions.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                hash.Add(exclusion, StringComparer.OrdinalIgnoreCase);
            }

            foreach (CleanerRuleDefinition rule in rules
                .OrderBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase))
            {
                hash.Add(rule.Id, StringComparer.OrdinalIgnoreCase);
                hash.Add(rule.Name, StringComparer.OrdinalIgnoreCase);
                hash.Add(rule.Category, StringComparer.OrdinalIgnoreCase);
                hash.Add(rule.ScanKind);
                hash.Add(rule.ExecutionMode);
                hash.Add(rule.SystemAction);
                hash.Add(rule.DefaultSelected);
                hash.Add(rule.RequiresElevation);
                hash.Add(rule.ViewOnly);
                hash.Add(rule.IncludeSubdirectories);
                hash.Add(rule.MinAgeDays);
                hash.Add(rule.MaxAgeDays);
                hash.Add(rule.PreflightLockCheck);

                foreach (string path in rule.Paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string expandedPath = CleanerKnownPathResolver.Expand(path);
                    if (!IsPathOnSelectedDrive(expandedPath, selectedDriveRoots))
                    {
                        continue;
                    }

                    hash.Add(path, StringComparer.OrdinalIgnoreCase);
                    AppendFilesystemFingerprint(
                        ref hash,
                        expandedPath,
                        ref fingerprintedEntries,
                        maxFingerprintEntries,
                        cancellationToken);
                }

                foreach (string pattern in rule.IncludePatterns.OrderBy(pattern => pattern, StringComparer.OrdinalIgnoreCase))
                {
                    hash.Add(pattern, StringComparer.OrdinalIgnoreCase);
                }

                foreach (string processName in rule.ProcessNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                {
                    hash.Add(processName, StringComparer.OrdinalIgnoreCase);
                }

                foreach (string root in rule.BoundaryRoots.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    hash.Add(root, StringComparer.OrdinalIgnoreCase);
                }
            }

            return hash.ToHashCode().ToString("X8");
        }

        private static Task<string> BuildQuickFingerprintAsync(
            IReadOnlyList<CleanerRuleDefinition> rules,
            HashSet<string> exclusions,
            HashSet<string> selectedDriveRoots,
            bool isElevated,
            CancellationToken cancellationToken)
        {
            // 指纹可能遍历十万级文件，必须明确离开 UI 线程，并允许用户随时取消。
            return Task.Run(
                () => BuildQuickFingerprint(rules, exclusions, selectedDriveRoots, isElevated, cancellationToken),
                cancellationToken);
        }

        private static HashSet<string> ResolveSelectedDriveRoots(IEnumerable<string> requestedRoots)
        {
            HashSet<string> roots = requestedRoots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(CleanerKnownPathResolver.Expand)
                .Select(path => Path.GetPathRoot(path) ?? string.Empty)
                .Select(CleanerPathSafety.NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (roots.Count > 0)
            {
                return roots;
            }

            string defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string defaultRoot = CleanerPathSafety.NormalizePath(
                Path.GetPathRoot(defaultPath) ?? Path.GetPathRoot(Environment.CurrentDirectory) ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(defaultRoot))
            {
                roots.Add(defaultRoot);
            }

            return roots;
        }

        private static bool IsPathOnSelectedDrive(string path, HashSet<string> selectedDriveRoots)
        {
            if (string.IsNullOrWhiteSpace(path) || selectedDriveRoots.Count == 0)
            {
                return false;
            }

            try
            {
                string pathRoot = CleanerPathSafety.NormalizePath(Path.GetPathRoot(path) ?? string.Empty);
                return selectedDriveRoots.Contains(pathRoot);
            }
            catch
            {
                return false;
            }
        }

        private static void AppendFilesystemFingerprint(
            ref HashCode hash,
            string rawPath,
            ref int entryCount,
            int maxEntries,
            CancellationToken cancellationToken)
        {
            try
            {
                string path = Path.GetFullPath(rawPath);
                if (File.Exists(path))
                {
                    FileInfo file = new(path);
                    hash.Add(CleanerPathSafety.NormalizePath(path), StringComparer.OrdinalIgnoreCase);
                    hash.Add(file.Length);
                    hash.Add(file.LastWriteTimeUtc.Ticks);
                    entryCount++;
                    return;
                }

                if (!Directory.Exists(path))
                {
                    hash.Add("missing", StringComparer.Ordinal);
                    return;
                }

                Stack<string> pending = new();
                pending.Push(path);
                while (pending.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string directory = pending.Pop();
                    DirectoryInfo directoryInfo = new(directory);
                    hash.Add(CleanerPathSafety.NormalizePath(directory), StringComparer.OrdinalIgnoreCase);
                    hash.Add(directoryInfo.LastWriteTimeUtc.Ticks);

                    foreach (string filePath in CleanerPathSafety.SafeEnumerateFiles(directory)
                                 .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++entryCount > maxEntries)
                        {
                            // 无法完整验证的大目录不进行复用，避免把过期大小呈现为当前结果。
                            hash.Add(DateTime.UtcNow.Ticks);
                            return;
                        }

                        try
                        {
                            FileInfo file = new(filePath);
                            hash.Add(CleanerPathSafety.NormalizePath(filePath), StringComparer.OrdinalIgnoreCase);
                            hash.Add(file.Length);
                            hash.Add(file.LastWriteTimeUtc.Ticks);
                        }
                        catch
                        {
                            hash.Add(DateTime.UtcNow.Ticks);
                            return;
                        }
                    }

                    foreach (string child in CleanerPathSafety.SafeEnumerateDirectories(directory)
                                 .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++entryCount > maxEntries)
                        {
                            hash.Add(DateTime.UtcNow.Ticks);
                            return;
                        }

                        if (CleanerPathSafety.IsReparsePoint(child))
                        {
                            DirectoryInfo linkInfo = new(child);
                            hash.Add(CleanerPathSafety.NormalizePath(child), StringComparer.OrdinalIgnoreCase);
                            hash.Add(linkInfo.LastWriteTimeUtc.Ticks);
                            continue;
                        }

                        pending.Push(child);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 读取不完整时使用一次性指纹，本轮仍可扫描，但下一轮不会错误复用。
                hash.Add(DateTime.UtcNow.Ticks);
            }
        }

        private sealed class ScanStats
        {
            public long SizeBytes { get; init; }
            public int FileCount { get; init; }
            public DateTimeOffset ModifyTime { get; init; }
            public bool IsLocked { get; init; }
            public List<string> LockProbePaths { get; init; } = new();
            public List<string> TargetPaths { get; init; } = new();
        }

        private sealed class CachedQuickScanSegment
        {
            public string Fingerprint { get; init; } = string.Empty;
            public DateTimeOffset CapturedAt { get; init; }
            public List<CleanerScanItem> Items { get; init; } = new();
        }

    }
}

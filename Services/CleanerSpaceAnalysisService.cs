using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerSpaceAnalysisService
    {
        private readonly CleanerRiskEvaluator _riskEvaluator;
        private readonly CleanerLockService _lockService;
        private readonly long _largeDirectoryThresholdBytes;
        private readonly long _largeFileThresholdBytes;
        private readonly int _maxCandidateDirectoriesPerRoot;
        private readonly int _maxVisitedDirectories;
        private readonly int _maxVisitedFiles;
        private readonly AIClassifierService? _aiClassifier;

        public CleanerSpaceAnalysisService(
            CleanerRiskEvaluator riskEvaluator,
            CleanerLockService lockService,
            AIClassifierService? aiClassifier = null,
            long largeDirectoryThresholdBytes = 256L * 1024L * 1024L,
            long largeFileThresholdBytes = 512L * 1024L * 1024L,
            int maxCandidateDirectoriesPerRoot = 24,
            int maxVisitedDirectories = 1024,
            int maxVisitedFiles = 50000)
        {
            _riskEvaluator = riskEvaluator;
            _lockService = lockService;
            _largeDirectoryThresholdBytes = largeDirectoryThresholdBytes;
            _largeFileThresholdBytes = largeFileThresholdBytes;
            _maxCandidateDirectoriesPerRoot = Math.Max(1, maxCandidateDirectoriesPerRoot);
            _maxVisitedDirectories = Math.Max(32, maxVisitedDirectories);
            _maxVisitedFiles = Math.Max(256, maxVisitedFiles);
            _aiClassifier = aiClassifier;
        }

        public async Task<List<CleanerScanItem>> AnalyzeAsync(
            HashSet<string> exclusions,
            IReadOnlyList<string> selectedDriveRoots,
            CancellationToken cancellationToken)
        {
            var items = await Task.Run(() => AnalyzeCore(exclusions, selectedDriveRoots, cancellationToken), cancellationToken);
            await EnrichWithAIAsync(items, cancellationToken);
            return items;
        }

        private List<CleanerScanItem> AnalyzeCore(
            HashSet<string> exclusions,
            IReadOnlyList<string> selectedDriveRoots,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<string> roots = CleanerAnalysisPathPlanner.BuildAnalysisRoots(selectedDriveRoots);
            List<(string Path, DirectoryAnalysisStats Stats)> largeDirectories = new();
            List<LargeFileCandidate> largeFiles = new();

            foreach (string root in roots.Where(Directory.Exists))
            {
                int candidateCount = 0;
                foreach (string child in CleanerAnalysisPathPlanner.EnumerateCandidates(root))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (candidateCount >= _maxCandidateDirectoriesPerRoot)
                    {
                        break;
                    }

                    string normalizedChild = CleanerPathSafety.NormalizePath(child);
                    if (string.IsNullOrWhiteSpace(normalizedChild) ||
                        CleanerPathSafety.IsExcluded(normalizedChild, exclusions) ||
                        CleanerPathSafety.IsReparsePoint(normalizedChild))
                    {
                        continue;
                    }

                    candidateCount++;
                    DirectoryAnalysisStats stats = AggregateDirectorySafely(normalizedChild, exclusions, cancellationToken);
                    if (stats.FileCount <= 0 || stats.SizeBytes <= 0)
                    {
                        continue;
                    }

                    if (stats.SizeBytes >= _largeDirectoryThresholdBytes)
                    {
                        largeDirectories.Add((normalizedChild, stats));
                    }

                    largeFiles.AddRange(stats.LargestFiles);
                }
            }

            List<CleanerScanItem> results = largeDirectories
                .OrderByDescending(candidate => candidate.Stats.SizeBytes)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .Select(BuildLargeDirectoryItem)
                .ToList();

            results.AddRange(largeFiles
                .Where(file => file.SizeBytes >= _largeFileThresholdBytes)
                .OrderByDescending(file => file.SizeBytes)
                .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(BuildLargeFileItem));

            return results;
        }

        private DirectoryAnalysisStats AggregateDirectorySafely(
            string root,
            HashSet<string> exclusions,
            CancellationToken cancellationToken)
        {
            long sizeBytes = 0;
            int fileCount = 0;
            int visitedDirectories = 0;
            int visitedFiles = 0;
            bool isLocked = false;
            DateTimeOffset lastModified = DateTimeOffset.MinValue;
            List<LargeFileCandidate> largestFiles = new();
            Stack<string> pending = new();
            pending.Push(root);

            while (pending.Count > 0 && visitedDirectories < _maxVisitedDirectories && visitedFiles < _maxVisitedFiles)
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

                    if (visitedFiles >= _maxVisitedFiles)
                    {
                        break;
                    }

                    if (CleanerPathSafety.IsExcluded(file, exclusions))
                    {
                        continue;
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

                        bool locked = CleanerPathSafety.IsFileLocked(file);
                        isLocked |= locked;
                        RegisterLargeFileCandidate(largestFiles, file, info.Length, fileWriteTime, locked);
                    }
                    catch
                    {
                    }
                }

                foreach (string directory in CleanerPathSafety.SafeEnumerateDirectories(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (visitedDirectories >= _maxVisitedDirectories)
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

            return new DirectoryAnalysisStats
            {
                SizeBytes = sizeBytes,
                FileCount = fileCount,
                ModifyTime = lastModified,
                IsLocked = isLocked,
                LargestFiles = largestFiles
            };
        }

        private CleanerScanItem BuildLargeDirectoryItem((string Path, DirectoryAnalysisStats Stats) candidate)
        {
            CleanerRiskAssessment risk = _riskEvaluator.Evaluate(
                null,
                candidate.Path,
                candidate.Stats.IsLocked,
                candidate.Stats.ModifyTime,
                candidate.Stats.SizeBytes);

            string name = Path.GetFileName(candidate.Path);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = candidate.Path;
            }

            return new CleanerScanItem
            {
                RuleId = "analysis_large_directory",
                Name = name,
                Description = "抽样空间分析发现的大目录，仅供查看，不参与一键清理。",
                Category = "unknown_large",
                Path = candidate.Path,
                SizeBytes = candidate.Stats.SizeBytes,
                FileCount = candidate.Stats.FileCount,
                ModifyTime = candidate.Stats.ModifyTime,
                OwnerApp = "分析结果",
                RiskLevel = CleanerRiskLevel.High,
                CleanScore = Math.Min(risk.Score, 30),
                ExecutionMode = CleanerExecutionMode.None,
                ScanKind = CleanerScanKind.Directory,
                IsLocked = candidate.Stats.IsLocked,
                DefaultSelected = false,
                ViewOnly = true,
                WhyItConsumesSpace = "这是当前选中磁盘中在抽样分析阶段识别出的高占用目录。",
                WhyItCanBeCleaned = "用途不确定，因此默认仅展示，不自动删除。",
                ImpactAfterCleanup = "建议先打开目录确认内容来源。",
                RegenerationHint = "不做自动清理，避免误删用户数据。",
                RiskSummary = "高风险：抽样分析命中的未知大目录默认只查看，不纳入一键清理",
                RiskDetail = "这不是全盘穷举结果，建议打开目录确认来源后再手动处理",
                CanSelect = false,
                IsSelected = false,
                IsExcluded = false
            };
        }

        private CleanerScanItem BuildLargeFileItem(LargeFileCandidate file)
        {
            return new CleanerScanItem
            {
                RuleId = "analysis_large_file",
                Name = Path.GetFileName(file.Path),
                Description = "抽样空间分析发现的大文件，仅供查看，不参与一键清理。",
                Category = "unknown_large_file",
                Path = file.Path,
                SizeBytes = file.SizeBytes,
                FileCount = 1,
                ModifyTime = file.ModifyTime,
                OwnerApp = "分析结果",
                RiskLevel = CleanerRiskLevel.High,
                CleanScore = 20,
                ExecutionMode = CleanerExecutionMode.None,
                ScanKind = CleanerScanKind.File,
                IsLocked = file.IsLocked,
                LockedByProcesses = file.IsLocked
                    ? _lockService.GetLockingProcesses([file.Path]).ToList()
                    : new List<string>(),
                DefaultSelected = false,
                ViewOnly = true,
                WhyItConsumesSpace = "这是当前选中磁盘中在抽样分析阶段识别出的高占用文件。",
                WhyItCanBeCleaned = "用途未知，因此默认只展示，不自动删除。",
                ImpactAfterCleanup = "建议先打开位置确认是否属于用户资料、素材或项目文件。",
                RegenerationHint = "不做自动清理，避免误删真实业务数据。",
                RiskSummary = "高风险：抽样分析命中的大文件默认只查看，不纳入一键清理",
                RiskDetail = "这不是全盘穷举结果，建议确认来源后手动处理",
                CanSelect = false,
                IsSelected = false,
                IsExcluded = false
            };
        }

        private static void RegisterLargeFileCandidate(
            List<LargeFileCandidate> candidates,
            string path,
            long sizeBytes,
            DateTimeOffset modifyTime,
            bool isLocked)
        {
            candidates.Add(new LargeFileCandidate
            {
                Path = path,
                SizeBytes = sizeBytes,
                ModifyTime = modifyTime,
                IsLocked = isLocked
            });

            if (candidates.Count <= 12)
            {
                return;
            }

            LargeFileCandidate? smallest = candidates
                .OrderBy(candidate => candidate.SizeBytes)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (smallest != null)
            {
                candidates.Remove(smallest);
            }
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

        private sealed class DirectoryAnalysisStats
        {
            public long SizeBytes { get; init; }
            public int FileCount { get; init; }
            public DateTimeOffset ModifyTime { get; init; }
            public bool IsLocked { get; init; }
            public List<LargeFileCandidate> LargestFiles { get; init; } = new();
        }

        private sealed class LargeFileCandidate
        {
            public string Path { get; init; } = string.Empty;
            public long SizeBytes { get; init; }
            public DateTimeOffset ModifyTime { get; init; }
            public bool IsLocked { get; init; }
        }

        private async Task EnrichWithAIAsync(List<CleanerScanItem> items, CancellationToken cancellationToken)
        {
            if (_aiClassifier == null) return;

            var targetItems = items.Where(i => i.ViewOnly && i.ScanKind == CleanerScanKind.Directory).ToList();
            var tasks = targetItems.Select(async item =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var classification = await _aiClassifier.ClassifyDirectoryAsync(item.Path, item.SizeBytes, cancellationToken);
                    if (classification == null || !classification.SafeToClean) return;

                item.Category = classification.Category;
                item.Name = classification.Name;
                item.Description = classification.Description;
                item.OwnerApp = classification.Category switch
                {
                    "dev_cache" => "开发工具缓存",
                    "app_cache" => "应用缓存",
                    "system_temp" => "系统临时文件",
                    "app_logs" => "应用日志",
                    _ => "AI 识别"
                };
                item.RiskLevel = classification.RiskLevel switch
                {
                    "Low" => CleanerRiskLevel.Low,
                    "Medium" => CleanerRiskLevel.Medium,
                    _ => CleanerRiskLevel.High
                };
                item.ExecutionMode = CleanerExecutionMode.Quarantine;
                item.ViewOnly = false;
                item.CanSelect = true;
                item.DefaultSelected = item.RiskLevel == CleanerRiskLevel.Low;
                item.WhyItConsumesSpace = classification.Description;
                item.WhyItCanBeCleaned = classification.CleanReason;
                item.ImpactAfterCleanup = item.RiskLevel == CleanerRiskLevel.Low
                    ? "可安全清理，需要时会自动重建。"
                    : "清理后相关功能可能需要重新配置。";
                item.RegenerationHint = "相关程序在需要时会重新生成。";
                item.RiskSummary = classification.RiskLevel switch
                {
                    "Low" => "低风险：AI 识别为可安全清理的开发/系统缓存",
                    "Medium" => "中风险：AI 识别为日志或临时数据",
                    _ => "高风险：AI 无法确认安全性"
                };
                item.RiskDetail = $"AI 自动分类: {classification.Description}";
                item.CleanScore = classification.RiskLevel switch
                {
                    "Low" => 90,
                    "Medium" => 60,
                    _ => 30
                };
                }
                catch
                {
                    // Ignore individual failures to not disrupt others
                }
            });

            await Task.WhenAll(tasks);
        }

    }
}
using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Services
{
    public sealed class CleanerDeepScanService
    {
        private readonly CleanerScanService _scanService;
        private readonly CleanerStateStore _stateStore;
        private readonly CleanerSpaceAnalysisService _spaceAnalysisService;
        private readonly CleanerOrphanResidueService _orphanResidueService;
        private readonly ILogger<CleanerDeepScanService> _logger;

        public CleanerDeepScanService(
            CleanerScanService scanService,
            CleanerStateStore stateStore,
            CleanerSpaceAnalysisService spaceAnalysisService,
            CleanerOrphanResidueService orphanResidueService,
            ILogger<CleanerDeepScanService> logger)
        {
            _scanService = scanService;
            _stateStore = stateStore;
            _spaceAnalysisService = spaceAnalysisService;
            _orphanResidueService = orphanResidueService;
            _logger = logger;
        }

        public async Task<CleanerDeepScanResult> ScanAsync(
            CleanerScanOptions options,
            IProgress<CleanerScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("[DeepScan] 开始深度扫描协调。");
            CleanerScanReport coreReport = await _scanService.ScanAsync(
                CleanerScanScope.Deep,
                BuildCoreOptions(options),
                MapProgress(progress, 0, 75),
                cancellationToken);
            _logger.LogInformation("[DeepScan] 核心深扫完成，命中 {Count} 项。", coreReport.Items.Count);

            HashSet<string> exclusions = await LoadExclusionsAsync();
            List<CleanerScanItem> combinedItems = coreReport.Items.ToList();

            CleanerScanAddOnResult spaceAnalysis = await RunSpaceAnalysisAsync(
                options,
                exclusions,
                combinedItems,
                MapProgress(progress, 75, 90),
                cancellationToken);

            CleanerScanAddOnResult orphanResidue = await RunOrphanResidueAsync(
                options,
                coreReport.AnalysisDriveRoots,
                exclusions,
                combinedItems,
                MapProgress(progress, 90, 100),
                cancellationToken);

            _logger.LogInformation("[DeepScan] 附加分析完成，合并后 {CombinedCount} 项；抽样补充 {AddedSpaceCount}，残留补充 {AddedOrphanCount}。", 
                combinedItems.Count, spaceAnalysis.AddedCount, orphanResidue.AddedCount);

            return new CleanerDeepScanResult
            {
                Report = BuildFinalReport(coreReport, combinedItems),
                SpaceAnalysis = spaceAnalysis,
                OrphanResidue = orphanResidue
            };
        }

        private static IProgress<CleanerScanProgress>? MapProgress(
            IProgress<CleanerScanProgress>? target,
            double start,
            double end)
        {
            return target == null
                ? null
                : new MappedScanProgress(target, start, end);
        }

        private sealed class MappedScanProgress : IProgress<CleanerScanProgress>
        {
            private readonly IProgress<CleanerScanProgress> _target;
            private readonly double _start;
            private readonly double _range;

            public MappedScanProgress(IProgress<CleanerScanProgress> target, double start, double end)
            {
                _target = target;
                _start = start;
                _range = Math.Max(0, end - start);
            }

            public void Report(CleanerScanProgress value)
            {
                double ratio = value.ProgressMax > 0
                    ? Math.Clamp(value.ProgressValue / value.ProgressMax, 0, 1)
                    : 0;

                _target.Report(new CleanerScanProgress
                {
                    StageTitle = value.StageTitle,
                    Detail = value.Detail,
                    ProgressValue = _start + (_range * ratio),
                    ProgressMax = 100
                });
            }
        }

        private async Task<HashSet<string>> LoadExclusionsAsync()
        {
            IReadOnlyList<CleanerExclusionEntry> exclusions = await _stateStore.LoadExclusionsAsync();
            return exclusions
                .Select(entry => CleanerPathSafety.NormalizePath(entry.Path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<CleanerScanAddOnResult> RunSpaceAnalysisAsync(
            CleanerScanOptions options,
            HashSet<string> exclusions,
            List<CleanerScanItem> combinedItems,
            IProgress<CleanerScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (!options.IncludeLargeObjectAnalysis)
            {
                return CleanerScanAddOnResult.None;
            }

            progress?.Report(new CleanerScanProgress
            {
                StageTitle = "深度扫描",
                Detail = "正在生成抽样空间占用分析",
                ProgressValue = 0,
                ProgressMax = 1
            });

            try
            {
                List<CleanerScanItem> analysisItems = await _spaceAnalysisService.AnalyzeAsync(
                    exclusions,
                    options.AnalysisDriveRoots,
                    cancellationToken);

                int addedCount = AppendDistinctItems(combinedItems, analysisItems);
                _logger.LogInformation("[DeepScan] 抽样空间分析完成，新增 {Count} 项。", addedCount);
                progress?.Report(new CleanerScanProgress
                {
                    StageTitle = "深度扫描",
                    Detail = addedCount > 0
                        ? $"已补充 {addedCount} 项抽样空间占用分析结果"
                        : "未发现需要额外提示的大目录或大文件",
                    ProgressValue = 1,
                    ProgressMax = 1
                });

                return new CleanerScanAddOnResult
                {
                    Attempted = true,
                    AddedCount = addedCount
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                _logger.LogInformation("[DeepScan] 抽样空间分析失败，已跳过。");
                progress?.Report(new CleanerScanProgress
                {
                    StageTitle = "深度扫描",
                    Detail = "抽样空间占用分析已跳过，不影响当前扫描结果",
                    ProgressValue = 1,
                    ProgressMax = 1
                });

                return new CleanerScanAddOnResult
                {
                    Attempted = true,
                    WasSkipped = true
                };
            }
        }

        private async Task<CleanerScanAddOnResult> RunOrphanResidueAsync(
            CleanerScanOptions options,
            IReadOnlyCollection<string> selectedDriveRoots,
            HashSet<string> exclusions,
            List<CleanerScanItem> combinedItems,
            IProgress<CleanerScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (!options.IncludeOrphanResidueAnalysis)
            {
                return CleanerScanAddOnResult.None;
            }

            progress?.Report(new CleanerScanProgress
            {
                StageTitle = "深度扫描",
                Detail = "正在识别疑似卸载残留提示",
                ProgressValue = 0,
                ProgressMax = 1
            });

            try
            {
                List<CleanerScanItem> orphanItems = await _orphanResidueService.ScanAsync(
                    exclusions,
                    selectedDriveRoots,
                    cancellationToken);
                int addedCount = AppendDistinctItems(combinedItems, orphanItems);
                _logger.LogInformation("[DeepScan] 疑似残留识别完成，新增 {Count} 项。", addedCount);
                progress?.Report(new CleanerScanProgress
                {
                    StageTitle = "深度扫描",
                    Detail = addedCount > 0
                        ? $"已补充识别 {addedCount} 项疑似卸载残留提示"
                        : "未发现需要额外提示的疑似卸载残留",
                    ProgressValue = 1,
                    ProgressMax = 1
                });

                return new CleanerScanAddOnResult
                {
                    Attempted = true,
                    AddedCount = addedCount
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                _logger.LogInformation("[DeepScan] 疑似残留识别失败，已跳过。");
                progress?.Report(new CleanerScanProgress
                {
                    StageTitle = "深度扫描",
                    Detail = "卸载残留提示已跳过，不影响当前扫描结果",
                    ProgressValue = 1,
                    ProgressMax = 1
                });

                return new CleanerScanAddOnResult
                {
                    Attempted = true,
                    WasSkipped = true
                };
            }
        }

        private static int AppendDistinctItems(List<CleanerScanItem> target, IEnumerable<CleanerScanItem> candidates)
        {
            int added = 0;
            HashSet<string> keys = target
                .Select(BuildKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (CleanerScanItem candidate in candidates)
            {
                if (!keys.Add(BuildKey(candidate)))
                {
                    continue;
                }

                target.Add(candidate);
                added++;
            }

            return added;
        }

        private static string BuildKey(CleanerScanItem item)
        {
            return $"{item.RuleId}|{CleanerPathSafety.NormalizePath(item.Path)}";
        }

        private static CleanerScanOptions BuildCoreOptions(CleanerScanOptions options)
        {
            return new CleanerScanOptions
            {
                AnalysisDriveRoots = options.AnalysisDriveRoots.ToList(),
                IncludeLargeObjectAnalysis = false,
                IncludeOrphanResidueAnalysis = false
            };
        }

        private static CleanerScanReport BuildFinalReport(CleanerScanReport coreReport, List<CleanerScanItem> combinedItems)
        {
            DateTimeOffset completedAt = DateTimeOffset.Now;
            DateTimeOffset startedAt = coreReport.CreatedAt - coreReport.Duration;

            return new CleanerScanReport
            {
                CreatedAt = completedAt,
                Scope = CleanerScanScope.Deep,
                Duration = completedAt - startedAt,
                AnalysisDriveRoots = coreReport.AnalysisDriveRoots.ToList(),
                UsedIncrementalReuse = coreReport.UsedIncrementalReuse,
                ReusedItemCount = coreReport.ReusedItemCount,
                Items = combinedItems
                    .OrderByDescending(item => item.SizeBytes)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }
    }
}

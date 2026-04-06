using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerDeepScanService
    {
        private readonly CleanerScanService _scanService;
        private readonly CleanerStateStore _stateStore;
        private readonly CleanerSpaceAnalysisService _spaceAnalysisService;
        private readonly CleanerOrphanResidueService _orphanResidueService;

        public CleanerDeepScanService(
            CleanerScanService scanService,
            CleanerStateStore stateStore,
            CleanerSpaceAnalysisService spaceAnalysisService,
            CleanerOrphanResidueService orphanResidueService)
        {
            _scanService = scanService;
            _stateStore = stateStore;
            _spaceAnalysisService = spaceAnalysisService;
            _orphanResidueService = orphanResidueService;
        }

        public async Task<CleanerDeepScanResult> ScanAsync(
            CleanerScanOptions options,
            IProgress<CleanerScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            CleanerDiagnosticsLogger.Trace("DeepScan", "开始深度扫描协调。");
            CleanerScanReport coreReport = await _scanService.ScanAsync(
                CleanerScanScope.Deep,
                BuildCoreOptions(options),
                progress,
                cancellationToken);
            CleanerDiagnosticsLogger.Trace("DeepScan", $"核心深扫完成，命中 {coreReport.Items.Count} 项。");

            HashSet<string> exclusions = await LoadExclusionsAsync();
            List<CleanerScanItem> combinedItems = coreReport.Items.ToList();

            CleanerScanAddOnResult spaceAnalysis = await RunSpaceAnalysisAsync(
                options,
                exclusions,
                combinedItems,
                progress,
                cancellationToken);

            CleanerScanAddOnResult orphanResidue = await RunOrphanResidueAsync(
                options,
                exclusions,
                combinedItems,
                progress,
                cancellationToken);

            CleanerDiagnosticsLogger.Trace(
                "DeepScan",
                $"附加分析完成，合并后 {combinedItems.Count} 项；抽样补充 {spaceAnalysis.AddedCount}，残留补充 {orphanResidue.AddedCount}。");

            return new CleanerDeepScanResult
            {
                Report = BuildFinalReport(coreReport, combinedItems),
                SpaceAnalysis = spaceAnalysis,
                OrphanResidue = orphanResidue
            };
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
                CleanerDiagnosticsLogger.Trace("DeepScan", $"抽样空间分析完成，新增 {addedCount} 项。");
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
                CleanerDiagnosticsLogger.Trace("DeepScan", "抽样空间分析失败，已跳过。");
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
                List<CleanerScanItem> orphanItems = await _orphanResidueService.ScanAsync(exclusions, cancellationToken);
                int addedCount = AppendDistinctItems(combinedItems, orphanItems);
                CleanerDiagnosticsLogger.Trace("DeepScan", $"疑似残留识别完成，新增 {addedCount} 项。");
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
                CleanerDiagnosticsLogger.Trace("DeepScan", "疑似残留识别失败，已跳过。");
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

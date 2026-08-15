using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueSapphire.Services
{
    /// <summary>
    /// 线程模型：可变状态全部在 _sync 锁内交换；CleanerScanReport 进出均深克隆，
    /// 订阅者与调用方拿到的是独立副本，可安全使用。
    /// AIMediaAnalysisContext / AIMediaOrganizationPreview 属性全部为 init-only，
    /// 按约定创建后不再修改（含其内部集合），因此直接共享引用；
    /// 若未来改为可变模型，必须同步改为克隆进出。
    /// </summary>
    public sealed class AISharedContextService
    {
        private readonly object _sync = new();
        private CleanerScanReport? _cleanerScan;
        private AIMediaAnalysisContext? _mediaAnalysis;
        private string? _currentMediaFolderPath;
        private AIMediaOrganizationPreview? _mediaOrganizationPreview;

        public event EventHandler<CleanerScanReport>? CleanerScanChanged;
        public event EventHandler<AIMediaAnalysisContext>? MediaAnalysisChanged;

        public void SetCleanerScan(CleanerScanReport report)
        {
            lock (_sync)
            {
                _cleanerScan = Clone(report);
            }
            CleanerScanChanged?.Invoke(this, Clone(report));
        }

        public CleanerScanReport? GetCleanerScan(TimeSpan? maximumAge = null)
        {
            lock (_sync)
            {
                if (_cleanerScan == null)
                {
                    return null;
                }
                if (maximumAge.HasValue &&
                    DateTimeOffset.Now - _cleanerScan.CreatedAt > maximumAge.Value)
                {
                    return null;
                }
                return Clone(_cleanerScan);
            }
        }

        public void SetMediaAnalysis(AIMediaAnalysisContext context)
        {
            lock (_sync)
            {
                _mediaAnalysis = context;
            }
            MediaAnalysisChanged?.Invoke(this, context);
        }

        public AIMediaAnalysisContext? GetMediaAnalysis(TimeSpan? maximumAge = null)
        {
            lock (_sync)
            {
                if (_mediaAnalysis == null)
                {
                    return null;
                }
                if (maximumAge.HasValue &&
                    DateTimeOffset.Now - _mediaAnalysis.CreatedAt > maximumAge.Value)
                {
                    return null;
                }
                return _mediaAnalysis;
            }
        }

        public void SetCurrentMediaFolder(string? folderPath)
        {
            lock (_sync)
            {
                _currentMediaFolderPath = string.IsNullOrWhiteSpace(folderPath)
                    ? null
                    : folderPath;
            }
        }

        public string? GetCurrentMediaFolder()
        {
            lock (_sync)
            {
                return _currentMediaFolderPath;
            }
        }

        public void SetMediaOrganizationPreview(AIMediaOrganizationPreview preview)
        {
            lock (_sync)
            {
                _mediaOrganizationPreview = preview;
            }
        }

        public AIMediaOrganizationPreview? GetMediaOrganizationPreview(TimeSpan maximumAge)
        {
            lock (_sync)
            {
                if (_mediaOrganizationPreview == null ||
                    DateTimeOffset.Now - _mediaOrganizationPreview.CreatedAt > maximumAge)
                {
                    return null;
                }
                return _mediaOrganizationPreview;
            }
        }

        private static CleanerScanReport Clone(CleanerScanReport report)
        {
            return new CleanerScanReport
            {
                CreatedAt = report.CreatedAt,
                Scope = report.Scope,
                Duration = report.Duration,
                AnalysisDriveRoots = report.AnalysisDriveRoots.ToList(),
                UsedIncrementalReuse = report.UsedIncrementalReuse,
                ReusedItemCount = report.ReusedItemCount,
                Items = report.Items.Select(CloneItem).ToList()
            };
        }

        private static CleanerScanItem CloneItem(CleanerScanItem source)
        {
            return new CleanerScanItem
            {
                ObjectId = source.ObjectId,
                RuleId = source.RuleId,
                Name = source.Name,
                Description = source.Description,
                Category = source.Category,
                Path = source.Path,
                TargetPaths = source.TargetPaths.ToList(),
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
                IncludePatterns = source.IncludePatterns.ToList(),
                IncludeSubdirectories = source.IncludeSubdirectories,
                MinAgeDays = source.MinAgeDays,
                MaxAgeDays = source.MaxAgeDays,
                IsLocked = source.IsLocked,
                DefaultSelected = source.DefaultSelected,
                RequiresElevation = source.RequiresElevation,
                IsElevatedMode = source.IsElevatedMode,
                BoundaryRoots = source.BoundaryRoots.ToList(),
                LockedByProcesses = source.LockedByProcesses.ToList(),
                ViewOnly = source.ViewOnly,
                WhyItConsumesSpace = source.WhyItConsumesSpace,
                WhyItCanBeCleaned = source.WhyItCanBeCleaned,
                ImpactAfterCleanup = source.ImpactAfterCleanup,
                RegenerationHint = source.RegenerationHint,
                RiskSummary = source.RiskSummary,
                RiskDetail = source.RiskDetail,
                AIAnalysisText = source.AIAnalysisText,
                AIRecommendationText = source.AIRecommendationText,
                CanSelect = source.CanSelect,
                IsSelected = source.IsSelected,
                IsExcluded = source.IsExcluded
            };
        }
    }
}

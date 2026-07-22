using BlueSapphire.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BlueSapphire.ViewModels
{
    public partial class CleanerAssistantViewModel
    {
        // ----------------------------------------------------
        // Privilege & Recovery Properties
        // ----------------------------------------------------
        public bool IsElevatedMode => _privilegeService.IsElevated;
        public bool CanEnterElevatedMode => !IsElevatedMode;
        public string PrivilegeModeText => IsElevatedMode ? "管理员模式" : "标准模式";
        public string PrivilegeModeHintText => IsElevatedMode
            ? "已获得最高权限，能够处理所有深层顽固文件。"
            : "当前为标准权限。遇到“访问被拒绝”的失败项时，可在此点击提权。";

        public bool CanElevateAndRetryFailures => !IsElevatedMode && Cleanup.HasRetryableFailures;
        public string FailureRecoveryElevateActionText => CanElevateAndRetryFailures ? "管理员模式并重试" : "进入管理员模式";

        public bool HasFailureRecoveryProcesses => GetFailureRecoveryProcesses().Count > 0;
        public string FailureRecoveryProcessText
        {
            get
            {
                var processes = GetFailureRecoveryProcesses();
                if (processes.Count == 0) return string.Empty;
                return $"发现 {processes.Count} 个可能占用文件的进程：{string.Join("、", processes.Take(3))}...";
            }
        }

        private List<string> GetFailureRecoveryProcesses()
        {
            return Cleanup.GetLatestFailedEntries()
                .Where(e => e.LockedByProcesses.Count > 0)
                .SelectMany(e => e.LockedByProcesses)
                .Distinct()
                .ToList();
        }

        // ----------------------------------------------------
        // Audit & History Properties
        // ----------------------------------------------------
        public string AuditScanSummaryText => Scan.AuditSnapshot?.ScanSummaryText ?? "暂无扫描记录";
        public string AuditCleanupSummaryText => Scan.AuditSnapshot?.CleanupSummaryText ?? "暂无清理记录";
        public string AuditRestoreSummaryText => Scan.AuditSnapshot?.RestoreSummaryText ?? "暂无恢复记录";
        public string AuditRetrySummaryText => Scan.AuditSnapshot?.RetrySummaryText ?? "暂无重试记录";
        public string AuditUserChoiceSummaryText => Scan.AuditSnapshot?.UserChoiceSummaryText ?? "暂无交互记录";
        public string AuditFailureSummaryText => Scan.AuditSnapshot?.TopFailureSummaryText ?? "暂无失败记录";

        // ----------------------------------------------------
        // Scan Trend Properties
        // ----------------------------------------------------
        private IReadOnlyList<CleanerScanSnapshot> GetRecentScans() =>
            (Scan.AuditSnapshot?.RecentScans ?? new List<CleanerScanSnapshot>())
            .OrderByDescending(scan => scan.CreatedAt)
            .ToList();
        
        public bool HasScanTrendHistory => GetRecentScans().Count > 1;
        public bool HasScanTrend => GetRecentScans().Count > 0;

        public string ScanTrendHeadlineText => HasScanTrendHistory ? "最近扫描趋势分析" : "扫描记录不足";
        public string ScanTrendDetailText => HasScanTrendHistory ? $"基于最近 {GetRecentScans().Count} 次扫描生成的分析" : "需要至少2次以上的扫描才能生成趋势分析。";
        public string ScanTrendDeltaText
        {
            get
            {
                IReadOnlyList<CleanerScanSnapshot> scans = GetRecentScans();
                if (scans.Count < 2) return "暂无可比较的上一次扫描";
                long delta = scans[0].TotalBytes - scans[1].TotalBytes;
                return delta == 0
                    ? "与上一次扫描相比，候选空间没有变化"
                    : $"与上一次相比，候选空间{(delta > 0 ? "增加" : "减少")} {CleanerSizeFormatter.Format(Math.Abs(delta))}";
            }
        }

        public string ScanTrendScopeText
        {
            get
            {
                IReadOnlyList<CleanerScanSnapshot> scans = GetRecentScans();
                return $"扫描类型：快速 {scans.Count(scan => scan.Scope == CleanerScanScope.Quick)} 次 · 深度 {scans.Count(scan => scan.Scope == CleanerScanScope.Deep)} 次";
            }
        }

        public string ScanTrendReuseText
        {
            get
            {
                IReadOnlyList<CleanerScanSnapshot> reused = GetRecentScans()
                    .Where(scan => scan.UsedIncrementalReuse)
                    .ToList();
                return reused.Count == 0
                    ? "缓存复用：最近扫描均为实时计算"
                    : $"缓存复用：{reused.Count} 次，共复用 {reused.Sum(scan => scan.ReusedItemCount)} 项";
            }
        }

        public string ScanTrendCompositionText
        {
            get
            {
                CleanerScanSnapshot? latest = GetRecentScans().FirstOrDefault();
                return latest == null
                    ? "暂无空间构成"
                    : $"最近构成：安全 {CleanerSizeFormatter.Format(latest.SafeBytes)} · 建议确认 {CleanerSizeFormatter.Format(latest.ReviewBytes)} · 仅查看 {CleanerSizeFormatter.Format(latest.ViewOnlyBytes)}";
            }
        }

        public string ScanTrendWindowText
        {
            get
            {
                IReadOnlyList<CleanerScanSnapshot> scans = GetRecentScans();
                if (scans.Count == 0) return "暂无时间范围";
                return scans.Count == 1
                    ? $"记录时间：{scans[0].CreatedAt.LocalDateTime:g}"
                    : $"统计范围：{scans[^1].CreatedAt.LocalDateTime:g} 至 {scans[0].CreatedAt.LocalDateTime:g}";
            }
        }
        
        public IReadOnlyList<CleanerScanTrendEntry> ScanTrendEntries
        {
            get
            {
                IReadOnlyList<CleanerScanSnapshot> scans = GetRecentScans();
                List<CleanerScanTrendEntry> entries = new();
                for (int index = 0; index < scans.Count; index++)
                {
                    CleanerScanSnapshot scan = scans[index];
                    CleanerScanSnapshot? previous = index + 1 < scans.Count ? scans[index + 1] : null;
                    long delta = previous == null ? 0 : scan.TotalBytes - previous.TotalBytes;
                    entries.Add(new CleanerScanTrendEntry
                    {
                        CreatedAt = scan.CreatedAt,
                        TimestampText = scan.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                        ScopeText = $"{scan.ScopeText} · {scan.TotalItemCount} 项 · {scan.DurationMs / 1000d:F1} 秒",
                        TotalText = CleanerSizeFormatter.Format(scan.TotalBytes),
                        DeltaText = previous == null
                            ? "最早记录"
                            : delta == 0
                                ? "较前一次无变化"
                                : $"较前一次{(delta > 0 ? "增加" : "减少")} {CleanerSizeFormatter.Format(Math.Abs(delta))}",
                        CompositionText = $"安全 {CleanerSizeFormatter.Format(scan.SafeBytes)} · 确认 {CleanerSizeFormatter.Format(scan.ReviewBytes)} · 查看 {CleanerSizeFormatter.Format(scan.ViewOnlyBytes)}",
                        DriveSummaryText = $"范围：{scan.DriveSummaryText}",
                        ReuseText = scan.UsedIncrementalReuse
                            ? $"已验证并复用 {scan.ReusedItemCount} 项"
                            : "实时扫描"
                    });
                }

                return entries;
            }
        }

        // ----------------------------------------------------
        // Recommendations
        // ----------------------------------------------------
        private CleanerRecommendationSummary _recommendationSummary = new();
        public bool HasRecommendationEntries => _recommendationSummary.Entries.Count > 0;
        public IReadOnlyList<CleanerRecommendationEntry> RecommendationEntries => _recommendationSummary.Entries;

        public string RecommendationHeadlineText => string.IsNullOrWhiteSpace(_recommendationSummary.Headline) ? "暂无智能建议" : _recommendationSummary.Headline;
        public string RecommendationDetailText => string.IsNullOrWhiteSpace(_recommendationSummary.Detail)
            ? "完成扫描后，这里会按你的空间结构、失败历史和偏好给出下一步建议。"
            : _recommendationSummary.Detail;
        public string RecommendationProfileText => string.IsNullOrWhiteSpace(_recommendationSummary.PreferenceModelText)
            ? "偏好模型：尚未建立"
            : _recommendationSummary.PreferenceModelText;

        public void RefreshRecommendationSummary()
        {
            if (Scan.AuditSnapshot != null)
            {
                _recommendationSummary = _recommendationService.BuildSummary(
                    Scan.AuditSnapshot,
                    Scan.AllItems.ToList(),
                    Rule.ProfileState,
                    Automation.Status,
                    Rule.RuleStatus);
            }
            OnPropertyChanged(nameof(HasRecommendationEntries));
            OnPropertyChanged(nameof(RecommendationEntries));
            OnPropertyChanged(nameof(RecommendationHeadlineText));
            OnPropertyChanged(nameof(RecommendationDetailText));
            OnPropertyChanged(nameof(RecommendationProfileText));
        }

        // ----------------------------------------------------
        // Hints
        // ----------------------------------------------------
        public string SystemBoundaryHintText => "系统级清理只允许命中规则声明的白名单根目录，例如 Windows Temp；即使已提权，也不会跨出这些边界。";
        public string ViewOnlyDisplayHintText => "仅供查看项不会被清理。";
        public bool HasHiddenViewOnlyItems => false;

        // ----------------------------------------------------
        // Selected Item (detail panel)
        // ----------------------------------------------------
        [ObservableProperty]
        public partial CleanerScanItem? SelectedScanItem { get; set; }

        public bool HasSelectedScanItem => SelectedScanItem != null;

        partial void OnSelectedScanItemChanged(CleanerScanItem? value)
        {
            OnPropertyChanged(nameof(HasSelectedScanItem));
        }

        [RelayCommand]
        private void SelectScanItem(CleanerScanItem? item)
        {
            SelectedScanItem = item;
        }

        // ----------------------------------------------------
        // Commands
        // ----------------------------------------------------
        [RelayCommand]
        private async Task OpenCleanerWorkspace()
        {
            await Cleanup.OpenQuarantineCommand.ExecuteAsync(null);
        }

        [RelayCommand]
        private async Task OpenItemLocation(CleanerScanItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path))
            {
                return;
            }

            bool opened = File.Exists(item.Path)
                ? await _nativeFileService.RevealInExplorerAsync(item.Path)
                : Directory.Exists(item.Path) && await _nativeFileService.OpenFolderAsync(item.Path);

            if (!opened && _view != null)
            {
                await _view.ShowTipAsync("无法打开位置", "目标已不存在或资源管理器无法访问该路径。");
            }
        }

        // We also need to hook into the UI refresh
        private void RaiseDashboardProperties()
        {
            OnPropertyChanged(nameof(IsElevatedMode));
            OnPropertyChanged(nameof(CanEnterElevatedMode));
            OnPropertyChanged(nameof(PrivilegeModeText));
            OnPropertyChanged(nameof(PrivilegeModeHintText));
            OnPropertyChanged(nameof(CanElevateAndRetryFailures));
            OnPropertyChanged(nameof(FailureRecoveryElevateActionText));
            OnPropertyChanged(nameof(HasFailureRecoveryProcesses));
            OnPropertyChanged(nameof(FailureRecoveryProcessText));
            
            OnPropertyChanged(nameof(AuditScanSummaryText));
            OnPropertyChanged(nameof(AuditCleanupSummaryText));
            OnPropertyChanged(nameof(AuditRestoreSummaryText));
            OnPropertyChanged(nameof(AuditRetrySummaryText));
            OnPropertyChanged(nameof(AuditUserChoiceSummaryText));
            OnPropertyChanged(nameof(AuditFailureSummaryText));
            
            OnPropertyChanged(nameof(HasScanTrendHistory));
            OnPropertyChanged(nameof(ScanTrendHeadlineText));
            OnPropertyChanged(nameof(ScanTrendDetailText));
            OnPropertyChanged(nameof(ScanTrendDeltaText));
            OnPropertyChanged(nameof(ScanTrendScopeText));
            OnPropertyChanged(nameof(ScanTrendReuseText));
            OnPropertyChanged(nameof(ScanTrendCompositionText));
            OnPropertyChanged(nameof(ScanTrendWindowText));
            OnPropertyChanged(nameof(ScanTrendEntries));

            RefreshRecommendationSummary();
        }
    }
}


using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueSapphire.Models
{
    public enum CleanerRiskLevel
    {
        Low,
        Medium,
        High
    }

    public enum CleanerExecutionMode
    {
        None,
        Recycle,
        Quarantine,
        Permanent
    }

    public enum CleanerScanKind
    {
        DirectoryContents,
        FilesByPattern,
        Directory,
        File
    }

    public enum CleanerScanScope
    {
        Quick,
        Deep
    }

    public enum CleanerFailureReason
    {
        None,
        InUse,
        AccessDenied,
        NotFound,
        ElevationRequired,
        BoundaryBlocked,
        ReparsePointSkipped,
        Unknown
    }

    public sealed class CleanerRuleManifest
    {
        public List<CleanerRuleDefinition> Rules { get; set; } = new();
    }

    public sealed class CleanerRuleBundleDocument
    {
        public string Version { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTimeOffset? PublishedAt { get; set; }
        public List<string> DisabledRuleIds { get; set; } = new();
        public List<CleanerRuleDefinition> Rules { get; set; } = new();
    }

    public sealed class CleanerRuleBundleStatus
    {
        public int BuiltInRuleCount { get; init; }
        public int EffectiveRuleCount { get; init; }
        public int ExternalRuleCount { get; init; }
        public int DisabledRuleCount { get; init; }
        public int RolloutFilteredRuleCount { get; init; }
        public int LocalDisabledRuleCount { get; init; }
        public bool HasExternalBundle { get; init; }
        public string BundleVersion { get; init; } = string.Empty;
        public string BundleSource { get; init; } = string.Empty;
        public DateTimeOffset? BundlePublishedAt { get; init; }
        public DateTimeOffset? LastRefreshedAt { get; init; }
        public string RemoteUri { get; init; } = string.Empty;
        public string ActiveRolloutChannel { get; init; } = string.Empty;
        public int DeviceBucket { get; init; }
    }

    public sealed class CleanerRuleDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public CleanerScanKind ScanKind { get; set; } = CleanerScanKind.DirectoryContents;
        public CleanerScanScope Scope { get; set; } = CleanerScanScope.Quick;
        public List<string> Paths { get; set; } = new();
        public List<string> IncludePatterns { get; set; } = new();
        public bool IncludeSubdirectories { get; set; }
        public CleanerExecutionMode ExecutionMode { get; set; } = CleanerExecutionMode.Quarantine;
        public CleanerRiskLevel RiskLevel { get; set; } = CleanerRiskLevel.Low;
        public bool DefaultSelected { get; set; } = true;
        public string OwnerApp { get; set; } = string.Empty;
        public string WhyItConsumesSpace { get; set; } = string.Empty;
        public string WhyItCanBeCleaned { get; set; } = string.Empty;
        public string ImpactAfterCleanup { get; set; } = string.Empty;
        public string RegenerationHint { get; set; } = string.Empty;
        public bool RequiresElevation { get; set; }
        public List<string> BoundaryRoots { get; set; } = new();
        public List<string> RolloutChannels { get; set; } = new();
        public int RolloutPercentage { get; set; } = 100;
        public string RolloutNote { get; set; } = string.Empty;
        public bool ViewOnly { get; set; }
    }

    public sealed class CleanerScanProgress
    {
        public string StageTitle { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public double ProgressValue { get; init; }
        public double ProgressMax { get; init; } = 100;
    }

    public sealed class CleanerExecutionProgress
    {
        public string StageTitle { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public double ProgressValue { get; init; }
        public double ProgressMax { get; init; } = 100;
    }

    public sealed class CleanerScanReport
    {
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
        public CleanerScanScope Scope { get; init; } = CleanerScanScope.Quick;
        public TimeSpan Duration { get; init; }
        public List<string> AnalysisDriveRoots { get; init; } = new();
        public bool UsedIncrementalReuse { get; init; }
        public int ReusedItemCount { get; init; }
        public List<CleanerScanItem> Items { get; init; } = new();
    }

    public sealed class CleanerScanOptions
    {
        public List<string> AnalysisDriveRoots { get; init; } = new();
        public bool IncludeLargeObjectAnalysis { get; init; } = true;
        public bool IncludeOrphanResidueAnalysis { get; init; } = true;
    }

    public sealed class CleanerScanAddOnResult
    {
        public static CleanerScanAddOnResult None { get; } = new();

        public bool Attempted { get; init; }
        public bool WasSkipped { get; init; }
        public int AddedCount { get; init; }
    }

    public sealed class CleanerDeepScanResult
    {
        public CleanerScanReport Report { get; init; } = new();
        public CleanerScanAddOnResult SpaceAnalysis { get; init; } = CleanerScanAddOnResult.None;
        public CleanerScanAddOnResult OrphanResidue { get; init; } = CleanerScanAddOnResult.None;
    }

    public sealed class CleanerScanSnapshot
    {
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
        public CleanerScanScope Scope { get; set; } = CleanerScanScope.Quick;
        public List<string> DriveRoots { get; set; } = new();
        public long DurationMs { get; set; }
        public int TotalItemCount { get; set; }
        public int SafeItemCount { get; set; }
        public int ReviewItemCount { get; set; }
        public int ViewOnlyItemCount { get; set; }
        public long TotalBytes { get; set; }
        public long SafeBytes { get; set; }
        public long ReviewBytes { get; set; }
        public long ViewOnlyBytes { get; set; }
        public bool UsedIncrementalReuse { get; set; }
        public int ReusedItemCount { get; set; }

        public string ScopeText => Scope == CleanerScanScope.Quick ? "快速扫描" : "深度扫描";
        public string DriveSummaryText => DriveRoots.Count == 0
            ? "默认系统范围"
            : string.Join(" / ", DriveRoots.Take(3));
    }

    public sealed class CleanerScanTrendEntry
    {
        public DateTimeOffset CreatedAt { get; init; }
        public string TimestampText { get; init; } = string.Empty;
        public string ScopeText { get; init; } = string.Empty;
        public string TotalText { get; init; } = string.Empty;
        public string DeltaText { get; init; } = string.Empty;
        public string CompositionText { get; init; } = string.Empty;
        public string DriveSummaryText { get; init; } = string.Empty;
        public string ReuseText { get; init; } = string.Empty;
    }

    public sealed class CleanerProfileState
    {
        public string DeviceProfileId { get; init; } = string.Empty;
        public string RolloutChannel { get; init; } = "stable";
        public int DeviceBucket { get; init; }
    }

    public sealed class CleanerRestoreSummary
    {
        public int RestoredCount { get; set; }
        public int FailedCount { get; set; }
        public long RestoredBytes { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class CleanerExclusionEntry
    {
        public string Path { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
        public string DisplayName => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        public string CreatedAtText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public sealed class CleanerPreferenceState
    {
        public List<string> SelectedDriveRoots { get; set; } = new();
        public bool ReminderEnabled { get; set; }
        public bool AutoLowRiskCleanupEnabled { get; set; }
        public int ReminderIntervalDays { get; set; } = 7;
        public DateTimeOffset? LastReminderAt { get; set; }
        public DateTimeOffset? LastAutoCleanupAt { get; set; }
        public DateTimeOffset? LastAutomationScheduleSyncAt { get; set; }
        public bool LastAutomationScheduleRegistered { get; set; }
        public string LastAutomationScheduleTaskName { get; set; } = string.Empty;
        public string LastAutomationScheduleError { get; set; } = string.Empty;
        public bool TelemetryEnabled { get; set; }
        public string TelemetryEndpoint { get; set; } = string.Empty;
        public DateTimeOffset? LastTelemetryUploadedAt { get; set; }
        public string LastTelemetryStatus { get; set; } = string.Empty;
        public string DeviceProfileId { get; set; } = string.Empty;
        public string RolloutChannel { get; set; } = "stable";
    }

    public sealed class CleanerAutomationScheduleState
    {
        public bool IsSupported { get; init; } = true;
        public bool IsConfigured { get; init; }
        public bool IsRegistered { get; init; }
        public string TaskName { get; init; } = string.Empty;
        public DateTimeOffset? LastSynchronizedAt { get; init; }
        public string ErrorMessage { get; init; } = string.Empty;
    }

    public sealed class CleanerAutomationStatus
    {
        public bool ReminderEnabled { get; init; }
        public bool AutoLowRiskCleanupEnabled { get; init; }
        public int ReminderIntervalDays { get; init; } = 7;
        public DateTimeOffset? LastReminderAt { get; init; }
        public DateTimeOffset? LastAutoCleanupAt { get; init; }
        public DateTimeOffset? NextReminderAt { get; init; }
        public DateTimeOffset? NextAutoCleanupAt { get; init; }
        public bool IsReminderDue { get; init; }
        public bool IsAutoCleanupDue { get; init; }
        public CleanerAutomationScheduleState ScheduleState { get; init; } = new();
    }

    public sealed class CleanerTelemetryStatus
    {
        public bool Enabled { get; init; }
        public string Endpoint { get; init; } = string.Empty;
        public DateTimeOffset? LastUploadedAt { get; init; }
        public string LastStatusText { get; init; } = string.Empty;
        public string RolloutChannel { get; init; } = "stable";
        public int DeviceBucket { get; init; }
        public bool CanUpload => Enabled && !string.IsNullOrWhiteSpace(Endpoint);
    }

    public sealed class CleanerRuleUpdateState
    {
        public string RemoteUri { get; set; } = string.Empty;
        public DateTimeOffset? LastRefreshedAt { get; set; }
        public string LastBundleVersion { get; set; } = string.Empty;
        public string LastBundleSource { get; set; } = string.Empty;
        public List<string> LocalDisabledRuleIds { get; set; } = new();
    }

    public sealed class CleanerDriveOption : ObservableObject
    {
        private bool _isSelected;

        public string RootPath { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string VolumeLabel { get; init; } = string.Empty;
        public string FileSystem { get; init; } = string.Empty;
        public long TotalBytes { get; init; }
        public long FreeBytes { get; init; }
        public bool IsSystemDrive { get; init; }
        public string DriveKindText { get; init; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string TitleText => string.IsNullOrWhiteSpace(VolumeLabel) ? Name : $"{Name} · {VolumeLabel}";
        public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
        public double UsedPercentage => TotalBytes <= 0 ? 0 : Math.Round((double)UsedBytes / TotalBytes * 100, 1);
        public string SubtitleText
        {
            get
            {
                string systemTag = IsSystemDrive ? "系统盘" : DriveKindText;
                string fileSystem = string.IsNullOrWhiteSpace(FileSystem) ? string.Empty : $" · {FileSystem}";
                return $"{systemTag}{fileSystem}";
            }
        }
        public string CapacityText => $"可用 {CleanerSizeFormatter.Format(FreeBytes)} / 总计 {CleanerSizeFormatter.Format(TotalBytes)}";
        public string UsageText => $"已用 {UsedPercentage:0.#}% · {CleanerSizeFormatter.Format(UsedBytes)}";
    }

    public sealed class CleanerCleanupBatch
    {
        public string BatchId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
        public CleanerScanScope Scope { get; set; } = CleanerScanScope.Quick;
        public int SelectedItemCount { get; set; }
        public long EstimatedBytes { get; set; }
        public long ReleasedBytes { get; set; }
        public List<CleanerCleanupEntry> Entries { get; set; } = new();
        public string SummaryText =>
            Entries.Count == 0
                ? "暂无清理记录"
                : $"{CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · 释放 {CleanerSizeFormatter.Format(ReleasedBytes)} · 成功 {CompletedCount} / 失败 {FailedCount}";
        public int CompletedCount => Entries.Count(entry => string.Equals(entry.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        public int FailedCount => Entries.Count(entry => string.Equals(entry.Status, "Failed", StringComparison.OrdinalIgnoreCase));
    }

    public sealed class CleanerCleanupEntry
    {
        public string EntryId { get; set; } = Guid.NewGuid().ToString("N");
        public string ItemId { get; set; } = string.Empty;
        public string RuleId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public CleanerExecutionMode ExecutionMode { get; set; } = CleanerExecutionMode.None;
        public CleanerRiskLevel RiskLevel { get; set; } = CleanerRiskLevel.High;
        public bool RequiresElevation { get; set; }
        public List<string> BoundaryRoots { get; set; } = new();
        public string Status { get; set; } = string.Empty;
        public bool CanRestore { get; set; }
        public bool Restored { get; set; }
        public DateTimeOffset? RestoredAt { get; set; }
        public string RestoredPath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public CleanerFailureReason FailureReason { get; set; } = CleanerFailureReason.None;
        public string FailureSummary => CleanerPresentation.ToFailureReasonText(FailureReason);
        public List<string> LockedByProcesses { get; set; } = new();
        public bool HasLockOwnerInfo => LockedByProcesses.Count > 0;
        public string LockedByText => LockedByProcesses.Count == 0 ? string.Empty : string.Join(" / ", LockedByProcesses.Take(3));
        public string RecoveryHint => CleanerPresentation.BuildFailureRecoveryHint(FailureReason, LockedByProcesses, RequiresElevation);
        public bool HasRecoveryHint => !string.IsNullOrWhiteSpace(RecoveryHint);
        public string SizeText => CleanerSizeFormatter.Format(SizeBytes);
        public string StatusText => CleanerPresentation.ToCleanupEntryStatusText(Status, FailureReason, Restored);
        public bool HasFailure => FailureReason != CleanerFailureReason.None || !string.IsNullOrWhiteSpace(ErrorMessage);
        public bool HasBackupPath => !string.IsNullOrWhiteSpace(BackupPath);
        public bool HasRuleId => !string.IsNullOrWhiteSpace(RuleId);
        public bool CanDisableRule => HasRuleId && HasFailure;
        public bool CanRestoreEntry => CanRestore && !Restored && string.Equals(Status, "Completed", StringComparison.OrdinalIgnoreCase);
        public bool CanRetryEntry
        {
            get
            {
                if (Restored || !string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return FailureReason switch
                {
                    CleanerFailureReason.None => true,
                    CleanerFailureReason.InUse => true,
                    CleanerFailureReason.AccessDenied => true,
                    CleanerFailureReason.ElevationRequired => true,
                    CleanerFailureReason.Unknown => true,
                    _ => false
                };
            }
        }
    }

    public sealed class CleanerAuditSnapshot
    {
        public int TotalScans { get; set; }
        public long LastScanDurationMs { get; set; }
        public long TotalScanDurationMs { get; set; }
        public int LastScanItemCount { get; set; }
        public int TotalCleanupRuns { get; set; }
        public long TotalReleasedBytes { get; set; }
        public int TotalCleanupFailures { get; set; }
        public int TotalRestoredItems { get; set; }
        public long TotalRestoredBytes { get; set; }
        public int TotalRetryRuns { get; set; }
        public int TotalRetryRecoveredItems { get; set; }
        public int TotalManualDeselections { get; set; }
        public Dictionary<string, int> RuleHits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> RuleCleanupSuccesses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> RuleFailures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> RuleDeselections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> FailureReasons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<CleanerScanSnapshot> RecentScans { get; set; } = new();

        public long AverageScanDurationMs => TotalScans <= 0 ? 0 : TotalScanDurationMs / TotalScans;
        public string ScanSummaryText
        {
            get
            {
                CleanerScanSnapshot? latest = RecentScans
                    .OrderByDescending(item => item.CreatedAt)
                    .FirstOrDefault();

                if (latest == null)
                {
                    return $"扫描 {TotalScans} 次 · 最近 {LastScanItemCount} 项";
                }

                string reuseText = latest.UsedIncrementalReuse && latest.ReusedItemCount > 0
                    ? $" · 复用 {latest.ReusedItemCount} 项"
                    : string.Empty;

                return $"扫描 {TotalScans} 次 · 最近 {latest.TotalItemCount} 项{reuseText}";
            }
        }
        public string CleanupSummaryText => $"清理 {TotalCleanupRuns} 次 · 累计释放 {CleanerSizeFormatter.Format(TotalReleasedBytes)}";
        public string RestoreSummaryText => $"恢复 {TotalRestoredItems} 项 · 回写 {CleanerSizeFormatter.Format(TotalRestoredBytes)}";
        public string RetrySummaryText => $"重试 {TotalRetryRuns} 次 · 挽回 {TotalRetryRecoveredItems} 项";
        public string UserChoiceSummaryText => $"手动取消默认勾选 {TotalManualDeselections} 项";
        public string TopFailureSummaryText
        {
            get
            {
                if (FailureReasons.Count == 0)
                {
                    return "暂无失败统计";
                }

                KeyValuePair<string, int> top = FailureReasons
                    .OrderByDescending(pair => pair.Value)
                    .First();

                return $"高频失败：{top.Key} · {top.Value} 次";
            }
        }
    }

    public sealed class CleanerRuleQualityEntry
    {
        public string RuleId { get; init; } = string.Empty;
        public string RuleName { get; init; } = string.Empty;
        public int HitCount { get; init; }
        public int CleanupSuccessCount { get; init; }
        public int FailureCount { get; init; }
        public int DeselectionCount { get; init; }
        public bool IsLocallyDisabled { get; init; }
        public int IssueScore => FailureCount * 4 + DeselectionCount * 2 + Math.Max(0, HitCount - CleanupSuccessCount - FailureCount);
        public string SummaryText => $"失败 {FailureCount} 次 · 取消勾选 {DeselectionCount} 次 · 命中 {HitCount} 次";
    }

    public sealed class CleanerDiagnosticReport
    {
        public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
        public CleanerRuleBundleStatus RuleStatus { get; init; } = new();
        public CleanerRuleUpdateState RuleUpdateState { get; init; } = new();
        public CleanerAuditSnapshot AuditSnapshot { get; init; } = new();
        public List<CleanerRuleQualityEntry> TopRuleIssues { get; init; } = new();
    }

    public sealed class CleanerRecommendationEntry
    {
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string ActionText { get; init; } = string.Empty;
    }

    public sealed class CleanerRecommendationSummary
    {
        public string Headline { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string PreferenceModelText { get; init; } = string.Empty;
        public List<CleanerRecommendationEntry> Entries { get; init; } = new();
    }

    public sealed class CleanerRiskAssessment
    {
        public int Score { get; init; }
        public CleanerRiskLevel RiskLevel { get; init; } = CleanerRiskLevel.High;
        public string Summary { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public bool CanSelect { get; init; }
    }

    public sealed class CleanerScanItem : ObservableObject
    {
        private bool _isSelected;
        private bool _isExcluded;

        public string ObjectId { get; init; } = Guid.NewGuid().ToString("N");
        public string RuleId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public int FileCount { get; init; }
        public DateTimeOffset ModifyTime { get; init; }
        public string OwnerApp { get; init; } = string.Empty;
        public CleanerRiskLevel RiskLevel { get; init; } = CleanerRiskLevel.High;
        public int CleanScore { get; init; }
        public CleanerExecutionMode ExecutionMode { get; init; } = CleanerExecutionMode.None;
        public CleanerScanKind ScanKind { get; init; } = CleanerScanKind.DirectoryContents;
        public bool IncludeSubdirectories { get; init; }
        public bool IsLocked { get; init; }
        public bool ViewOnly { get; init; }
        public bool CanSelect { get; init; }
        public bool DefaultSelected { get; init; }
        public bool RequiresElevation { get; init; }
        public bool IsElevatedMode { get; init; }
        public List<string> IncludePatterns { get; init; } = new();
        public List<string> TargetPaths { get; init; } = new();
        public List<string> BoundaryRoots { get; init; } = new();
        public string WhyItConsumesSpace { get; init; } = string.Empty;
        public string WhyItCanBeCleaned { get; init; } = string.Empty;
        public string ImpactAfterCleanup { get; init; } = string.Empty;
        public string RegenerationHint { get; init; } = string.Empty;
        public string RiskSummary { get; init; } = string.Empty;
        public string RiskDetail { get; init; } = string.Empty;
        public List<string> LockedByProcesses { get; init; } = new();

        public bool IsExcluded
        {
            get => _isExcluded;
            set
            {
                if (SetProperty(ref _isExcluded, value))
                {
                    if (value)
                    {
                        IsSelected = false;
                    }

                    OnPropertyChanged(nameof(IsSelectableAndEnabled));
                    OnPropertyChanged(nameof(StatusHintText));
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                bool nextValue = value && IsSelectableAndEnabled;
                if (SetProperty(ref _isSelected, nextValue))
                {
                    OnPropertyChanged(nameof(StatusHintText));
                }
            }
        }

        public bool IsSelectableAndEnabled => CanSelect && !IsExcluded && !ViewOnly && (!RequiresElevation || IsElevatedMode);
        public bool IsSafeBucket => RiskLevel == CleanerRiskLevel.Low && !ViewOnly;
        public bool IsReviewBucket => RiskLevel == CleanerRiskLevel.Medium && !ViewOnly;
        public bool IsViewOnlyBucket => ViewOnly || RiskLevel == CleanerRiskLevel.High || ExecutionMode == CleanerExecutionMode.None;
        public string SizeText => CleanerSizeFormatter.Format(SizeBytes);
        public string FileCountText => FileCount <= 0 ? "-" : $"{FileCount:N0} 个文件";
        public string ModifyTimeText => ModifyTime == DateTimeOffset.MinValue ? "-" : ModifyTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        public string CategoryText => CleanerPresentation.ToCategoryText(Category);
        public string RiskText => CleanerPresentation.ToRiskText(RiskLevel);
        public string ExecutionModeText => CleanerPresentation.ToExecutionModeText(ExecutionMode);
        public string SelectionText => IsSelected ? "已纳入本次清理" : "未纳入本次清理";
        public bool HasLockOwnerInfo => LockedByProcesses.Count > 0;
        public string LockedByText => LockedByProcesses.Count == 0 ? "未检测到占用进程" : $"占用进程：{string.Join(" / ", LockedByProcesses.Take(3))}";

        public string StatusHintText
        {
            get
            {
                if (IsExcluded)
                {
                    return "已加入排除列表，后续扫描将跳过";
                }

                if (RequiresElevation && !IsElevatedMode)
                {
                    return "需要管理员模式才能扫描和清理该系统级目录";
                }

                if (ViewOnly || ExecutionMode == CleanerExecutionMode.None)
                {
                    return "仅供查看，默认不执行删除";
                }

                if (IsLocked)
                {
                    return HasLockOwnerInfo
                        ? $"当前文件可能正被占用：{string.Join(" / ", LockedByProcesses.Take(3))}"
                        : "当前文件可能正被占用，建议先关闭相关程序";
                }

                return RiskLevel switch
                {
                    CleanerRiskLevel.Low => "安全：删除后通常会自动重新生成",
                    CleanerRiskLevel.Medium => "谨慎：建议确认后再清理",
                    _ => "高风险：建议先打开目录查看"
                };
            }
        }
    }

    public static class CleanerPresentation
    {
        public static string ToCategoryText(string category)
        {
            return category switch
            {
                "system_temp" => "系统临时文件",
                "app_cache" => "应用缓存",
                "app_logs" => "应用日志",
                "app_update_cache" => "更新缓存",
                "app_userdata" => "应用数据",
                "orphan_leftover" => "疑似残留目录",
                "media_cache" => "媒体预览缓存",
                "browser_cache" => "浏览器缓存",
                "unknown_large" => "大目录分析",
                "unknown_large_file" => "大文件分析",
                "risky_object" => "高风险对象",
                _ => "其他清理项"
            };
        }

        public static string ToRiskText(CleanerRiskLevel riskLevel)
        {
            return riskLevel switch
            {
                CleanerRiskLevel.Low => "低风险",
                CleanerRiskLevel.Medium => "建议确认",
                _ => "仅供查看"
            };
        }

        public static string ToExecutionModeText(CleanerExecutionMode executionMode)
        {
            return executionMode switch
            {
                CleanerExecutionMode.Recycle => "回收站",
                CleanerExecutionMode.Quarantine => "隔离区",
                CleanerExecutionMode.Permanent => "永久删除",
                _ => "不执行"
            };
        }

        public static string ToFailureReasonText(CleanerFailureReason reason)
        {
            return reason switch
            {
                CleanerFailureReason.InUse => "被占用",
                CleanerFailureReason.AccessDenied => "权限不足",
                CleanerFailureReason.NotFound => "对象不存在",
                CleanerFailureReason.ElevationRequired => "需要管理员模式",
                CleanerFailureReason.BoundaryBlocked => "超出清理边界",
                CleanerFailureReason.ReparsePointSkipped => "符号链接已跳过",
                CleanerFailureReason.Unknown => "未知失败",
                _ => "无"
            };
        }

        public static string BuildFailureRecoveryHint(
            CleanerFailureReason reason,
            IReadOnlyList<string>? lockedByProcesses,
            bool requiresElevation)
        {
            return reason switch
            {
                CleanerFailureReason.InUse => BuildInUseHint(lockedByProcesses),
                CleanerFailureReason.AccessDenied => requiresElevation
                    ? "先进入管理员模式；若仍失败，请检查原目录权限后再重试。"
                    : "请检查原目录权限后再重试。",
                CleanerFailureReason.NotFound => "原始目标已不存在，无需再次处理。",
                CleanerFailureReason.ElevationRequired => "先进入管理员模式，再重试该系统级对象。",
                CleanerFailureReason.BoundaryBlocked => "该项超出规则白名单边界，不能通过重试绕过。",
                CleanerFailureReason.ReparsePointSkipped => "该项是符号链接或 Junction，默认跳过以避免跨目录误删。",
                CleanerFailureReason.Unknown => "建议先重新扫描确认对象仍存在，再决定是否重试。",
                _ => string.Empty
            };
        }

        public static string ToCleanupEntryStatusText(string status, CleanerFailureReason reason, bool restored)
        {
            if (restored)
            {
                return "已恢复";
            }

            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return "已清理";
            }

            if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return reason == CleanerFailureReason.None
                    ? "清理失败"
                    : $"清理失败 · {ToFailureReasonText(reason)}";
            }

            if (string.Equals(status, "RestoreFailed", StringComparison.OrdinalIgnoreCase))
            {
                return "恢复失败";
            }

            if (string.Equals(status, "RestoreMissing", StringComparison.OrdinalIgnoreCase))
            {
                return "隔离区内容缺失";
            }

            return string.IsNullOrWhiteSpace(status) ? "未执行" : status;
        }

        private static string BuildInUseHint(IReadOnlyList<string>? lockedByProcesses)
        {
            List<string> owners = lockedByProcesses?
                .Where(process => !string.IsNullOrWhiteSpace(process))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList() ?? new List<string>();

            return owners.Count == 0
                ? "先关闭相关程序，再执行“重试失败项”。"
                : $"先关闭 {string.Join(" / ", owners)}，再执行“重试失败项”。";
        }
    }

    public static class CleanerSizeFormatter
    {
        public static string Format(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return unitIndex == 0 ? $"{value:0} {units[unitIndex]}" : $"{value:0.##} {units[unitIndex]}";
        }

        public static string BuildSelectionSummary(IEnumerable<CleanerScanItem> items)
        {
            var materialized = items.Where(item => item.IsSelected).ToList();
            long totalBytes = materialized.Sum(item => item.SizeBytes);
            return $"{materialized.Count} 项 · {Format(totalBytes)}";
        }
    }
}

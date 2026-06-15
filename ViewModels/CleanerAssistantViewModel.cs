using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.ViewModels
{
    public partial class CleanerAssistantViewModel : ObservableObject
    {
        private const int MaxDisplayedViewOnlyItems = 12;

        private readonly CleanerScanService _scanService;
        private readonly CleanerExecutionService _executionService;
        private readonly CleanerStateStore _stateStore;
        private readonly CleanerRuleService _ruleService;
        private readonly NativeFileService _nativeFileService;
        private readonly CleanerPrivilegeService _privilegeService;
        private readonly CleanerAuditService _auditService;
        private readonly CleanerAutomationService _automationService;
        private readonly CleanerLaunchActionService _launchActionService;
        private readonly CleanerDriveService _driveService;
        private readonly CleanerDeepScanService _deepScanService;
        private readonly CleanerProfileService _profileService;
        private readonly CleanerTelemetryService _telemetryService;
        private readonly CleanerRecommendationService _recommendationService;
        private readonly List<CleanerScanItem> _allItems = new();

        private ICleanerAssistantViewInteraction? _view;
        private DispatcherQueue? _dispatcherQueue;
        private CancellationTokenSource? _operationCts;
        private CleanerCleanupBatch? _latestBatch;
        private CleanerAuditSnapshot _auditSnapshot = new();
        private CleanerAutomationStatus _automationStatus = new();
        private CleanerRuleBundleStatus _ruleStatus = new();
        private CleanerRuleUpdateState _ruleUpdateState = new();
        private IReadOnlyList<CleanerRuleDefinition> _knownRules = Array.Empty<CleanerRuleDefinition>();
        private CleanerProfileState _profileState = new();
        private CleanerTelemetryStatus _telemetryStatus = new();
        private CleanerRecommendationSummary _recommendationSummary = new();
        private CleanerScanScope _lastScope = CleanerScanScope.Quick;
        private bool _isUpdatingDriveSelection;
        private int _dashboardRefreshSuspendCount;
        private bool _dashboardRefreshPending;

        public CleanerSettingsViewModel Settings { get; }

        public ObservableCollection<CleanerScanItem> SafeItems { get; } = new();
        public ObservableCollection<CleanerScanItem> ReviewItems { get; } = new();
        public ObservableCollection<CleanerScanItem> ViewOnlyItems { get; } = new();
        public ObservableCollection<CleanerExclusionEntry> Exclusions { get; } = new();
        public ObservableCollection<CleanerCleanupEntry> LatestCleanupEntries { get; } = new();
        public ObservableCollection<CleanerDriveOption> DriveOptions { get; } = new();

        private string _statusMainText = "等待扫描";
        public string StatusMainText
        {
            get => _statusMainText;
            set => SetProperty(ref _statusMainText, value);
        }

        private string _statusDetailText = "优先解释空间来源，再决定要不要清。";
        public string StatusDetailText
        {
            get => _statusDetailText;
            set => SetProperty(ref _statusDetailText, value);
        }

        private string _scanModeText = "尚未启动扫描";
        public string ScanModeText
        {
            get => _scanModeText;
            set => SetProperty(ref _scanModeText, value);
        }

        private string _lastScanText = "最近扫描：无";
        public string LastScanText
        {
            get => _lastScanText;
            set => SetProperty(ref _lastScanText, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private bool _canCancelCurrentOperation;
        public bool CanCancelCurrentOperation
        {
            get => _canCancelCurrentOperation;
            set => SetProperty(ref _canCancelCurrentOperation, value);
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        private double _progressMax = 100.0;

        public double ProgressMax
        {
            get => _progressMax;
            set => SetProperty(ref _progressMax, value);
        }

        public bool HasResults => _allItems.Count > 0;
        public bool HasSafeItems => _allItems.Any(item => item.IsSafeBucket);
        public bool HasReviewItems => _allItems.Any(item => item.IsReviewBucket);
        public bool HasViewOnlyItems => _allItems.Any(item => item.IsViewOnlyBucket);
        public bool HasExclusions => Exclusions.Count > 0;
        public bool HasRestorableBatch => _latestBatch?.Entries.Any(entry => entry.CanRestore && !entry.Restored) == true;
        public bool HasLatestCleanupEntries => LatestCleanupEntries.Count > 0;
        public bool HasLatestCleanupFailures => _latestBatch?.FailedCount > 0;
        public bool HasDriveOptions => DriveOptions.Count > 0;
        public bool HasExternalRulePack => _ruleStatus.HasExternalBundle;
        public bool HasLocallyDisabledRules => _ruleStatus.LocalDisabledRuleCount > 0;
        public bool CanRunCleanup => _allItems.Any(item => item.IsSelected);
        public bool IsElevatedMode => _privilegeService.IsElevated;
        public bool CanEnterElevatedMode => !IsElevatedMode;
        public bool HasRetryableFailures => _latestBatch?.Entries.Any(entry => entry.CanRetryEntry) == true;
        public bool CanElevateAndRetryFailures => !IsElevatedMode && GetLatestFailedEntries().Any(entry =>
            entry.CanRetryEntry &&
            (GetEffectiveFailureReason(entry) == CleanerFailureReason.ElevationRequired ||
             GetEffectiveFailureReason(entry) == CleanerFailureReason.AccessDenied));
        public bool HasFailureRecoveryProcesses => GetFailureRecoveryProcesses().Count > 0;
        public bool CanRunAutomaticLowRiskCleanupNow => !IsBusy;
        public bool IsStableRolloutSelected => string.Equals(_profileState.RolloutChannel, "stable", StringComparison.OrdinalIgnoreCase);
        public bool IsCanaryRolloutSelected => string.Equals(_profileState.RolloutChannel, "canary", StringComparison.OrdinalIgnoreCase);
        public bool IsInternalRolloutSelected => string.Equals(_profileState.RolloutChannel, "internal", StringComparison.OrdinalIgnoreCase);
        public bool CanChooseStableRollout => !IsStableRolloutSelected;
        public bool CanChooseCanaryRollout => !IsCanaryRolloutSelected;
        public bool CanChooseInternalRollout => !IsInternalRolloutSelected;
        public bool HasRecommendationEntries => _recommendationSummary.Entries.Count > 0;
        public bool CanUploadTelemetryNow => _telemetryStatus.CanUpload && !IsBusy;

        public string SafeSpaceText => CleanerSizeFormatter.Format(_allItems.Where(item => item.IsSafeBucket).Sum(item => item.SizeBytes));
        public string ReviewSpaceText => CleanerSizeFormatter.Format(_allItems.Where(item => item.IsReviewBucket).Sum(item => item.SizeBytes));
        public string ViewOnlySpaceText => CleanerSizeFormatter.Format(_allItems.Where(item => item.IsViewOnlyBucket).Sum(item => item.SizeBytes));
        public string SelectedSummaryText => CleanerSizeFormatter.BuildSelectionSummary(_allItems);
        public string SafeCountText => $"{_allItems.Count(item => item.IsSafeBucket)} 项";
        public string ReviewCountText => $"{_allItems.Count(item => item.IsReviewBucket)} 项";
        public string ViewOnlyCountText => $"{_allItems.Count(item => item.IsViewOnlyBucket)} 项";
        public bool HasHiddenViewOnlyItems => HiddenViewOnlyCount > 0;
        public int HiddenViewOnlyCount => Math.Max(0, _allItems.Count(item => item.IsViewOnlyBucket) - ViewOnlyItems.Count);
        public string ViewOnlyDisplayHintText => HiddenViewOnlyCount <= 0
            ? string.Empty
            : $"仅展示体积最大的 {ViewOnlyItems.Count} 项，其余 {HiddenViewOnlyCount} 项已折叠，避免深度分析结果一次性铺满页面。";
        public string SelectedDriveSummaryText
        {
            get
            {
                List<CleanerDriveOption> selected = GetSelectedDriveOptions();
                if (selected.Count == 0)
                {
                    return "未选择磁盘";
                }

                return $"已选 {selected.Count} 个磁盘 · {string.Join(" / ", selected.Select(option => option.Name))}";
            }
        }
        public string DriveSelectionHintText => "深度扫描会按这里选中的磁盘做空间分析；系统与应用规则仍按真实安装位置识别。";
        public string AutomationSummaryText => $"每 {Settings.ReminderIntervalDays} 天检查一次";
        public string AutomationModeText
        {
            get
            {
                if (Settings.AutoLowRiskCleanupEnabled && Settings.ReminderEnabled)
                {
                    return "到期后先执行自动低风险清理，同时刷新提醒周期。";
                }

                if (Settings.AutoLowRiskCleanupEnabled)
                {
                    return "到期后会自动执行一次快速低风险清理。";
                }

                if (Settings.ReminderEnabled)
                {
                    return "到期后只提醒，不会自动删除。";
                }

                return "当前未启用定时提醒或自动保洁。";
            }
        }
        public string AutomationNextActionText
        {
            get
            {
                if (Settings.AutoLowRiskCleanupEnabled)
                {
                    if (_automationStatus.IsAutoCleanupDue)
                    {
                        return "自动保洁：当前已到期，下次进入清理助手时会执行。";
                    }

                    return _automationStatus.NextAutoCleanupAt == null
                        ? "自动保洁：未启用"
                        : $"自动保洁：下次 {FormatScheduleTime(_automationStatus.NextAutoCleanupAt)}";
                }

                if (Settings.ReminderEnabled)
                {
                    if (_automationStatus.IsReminderDue)
                    {
                        return "清理提醒：当前已到期，下次进入时会提示你执行快速扫描。";
                    }

                    return _automationStatus.NextReminderAt == null
                        ? "清理提醒：未启用"
                        : $"清理提醒：下次 {FormatScheduleTime(_automationStatus.NextReminderAt)}";
                }

                return "定时保洁：已关闭";
            }
        }
        public string AutomationLastActionText
        {
            get
            {
                string reminderText = _automationStatus.LastReminderAt == null
                    ? "提醒：尚无记录"
                    : $"提醒：{FormatScheduleTime(_automationStatus.LastReminderAt)}";
                string cleanupText = _automationStatus.LastAutoCleanupAt == null
                    ? "自动保洁：尚无记录"
                    : $"自动保洁：{FormatScheduleTime(_automationStatus.LastAutoCleanupAt)}";
                return $"{reminderText} · {cleanupText}";
            }
        }
        public string AutomationHintText => "自动保洁只会执行快速扫描中默认勾选的低风险项，不会碰建议确认项、仅供查看项，也不会绕过提权和边界限制。";
        public string AutomationScheduleText
        {
            get
            {
                CleanerAutomationScheduleState schedule = _automationStatus.ScheduleState;
                if (!schedule.IsSupported)
                {
                    return "系统计划任务：当前环境不支持";
                }

                if (!schedule.IsConfigured)
                {
                    return "系统计划任务：未启用";
                }

                return schedule.IsRegistered
                    ? "系统计划任务：已注册"
                    : "系统计划任务：待修复";
            }
        }
        public string AutomationScheduleDetailText
        {
            get
            {
                CleanerAutomationScheduleState schedule = _automationStatus.ScheduleState;
                if (!schedule.IsSupported)
                {
                    return schedule.ErrorMessage;
                }

                if (!schedule.IsConfigured)
                {
                    return "当前未启用系统级计划触发；提醒和自动保洁仍会在你打开清理助手时按周期检查。";
                }

                string syncText = schedule.LastSynchronizedAt == null
                    ? "尚未同步"
                    : $"最近同步 {FormatScheduleTime(schedule.LastSynchronizedAt)}";

                if (schedule.IsRegistered)
                {
                    return $"任务名：{schedule.TaskName} · {syncText}";
                }

                string error = string.IsNullOrWhiteSpace(schedule.ErrorMessage) ? "计划任务尚未注册成功。" : schedule.ErrorMessage;
                return $"任务名：{schedule.TaskName} · {syncText} · {error}";
            }
        }
        public string RulePackSummaryText => $"有效规则 {_ruleStatus.EffectiveRuleCount} 条";
        public string RulePackDetailText => !_ruleStatus.HasExternalBundle
            ? $"当前仅使用内置规则库，共 {_ruleStatus.BuiltInRuleCount} 条。"
            : $"外部规则 {_ruleStatus.ExternalRuleCount} 条 · 停用规则 {_ruleStatus.DisabledRuleCount} 条";
        public string RulePackSourceText
        {
            get
            {
                if (!_ruleStatus.HasExternalBundle)
                {
                    return "规则来源：内置规则库";
                }

                string source = string.IsNullOrWhiteSpace(_ruleStatus.BundleSource) ? "外部规则包" : _ruleStatus.BundleSource;
                string version = string.IsNullOrWhiteSpace(_ruleStatus.BundleVersion) ? string.Empty : $" · {_ruleStatus.BundleVersion}";
                return $"规则来源：{source}{version}";
            }
        }
        public string RulePackRefreshText => _ruleStatus.LastRefreshedAt == null
            ? "最近刷新：未记录"
            : $"最近刷新：{_ruleStatus.LastRefreshedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
        public string RulePackRemoteUriText => string.IsNullOrWhiteSpace(_ruleStatus.RemoteUri) ? "未配置远程链接" : _ruleStatus.RemoteUri;
        public string RulePackHintText => $"外部规则包可新增规则、覆盖已有规则，并通过 disabledRuleIds 停用有问题的规则。本地已停用 {_ruleStatus.LocalDisabledRuleCount} 条规则，当前灰度通道 {FormatRolloutChannel(_ruleStatus.ActiveRolloutChannel)} · 设备桶 {_ruleStatus.DeviceBucket:00}。";
        public string RolloutSummaryText => $"灰度通道：{FormatRolloutChannel(_profileState.RolloutChannel)} · 设备桶 {_profileState.DeviceBucket:00}";
        public string RolloutDetailText
        {
            get
            {
                if (_ruleStatus.RolloutFilteredRuleCount <= 0)
                {
                    return "当前通道下没有额外被灰度拦住的规则，规则集已经全部生效。";
                }

                return $"当前有 {_ruleStatus.RolloutFilteredRuleCount} 条规则仍在灰度之外。切换通道后会重新计算有效规则并刷新当前扫描结果。";
            }
        }
        public string RolloutHintText => "Stable 适合正式使用；Canary 用于小流量验证；Internal 预留给更激进的实验规则。";
        public string TelemetrySummaryText => Settings.TelemetryEnabled ? "云端遥测：已启用" : "云端遥测：已关闭";
        public string TelemetryDetailText
        {
            get
            {
                if (!Settings.TelemetryEnabled)
                {
                    return "当前不会上传扫描、清理和规则质量摘要。启用后仍只会上报摘要，不会读取用户文件内容。";
                }

                return _telemetryStatus.CanUpload
                    ? "当前会上传规则命中、失败分类、趋势摘要和灰度状态，用于云端质量治理。"
                    : "已启用遥测，但还没有配置有效的上传地址。";
            }
        }
        public string TelemetryEndpointText => string.IsNullOrWhiteSpace(_telemetryStatus.Endpoint)
            ? "遥测地址：未配置"
            : $"遥测地址：{_telemetryStatus.Endpoint}";
        public string TelemetryLastUploadText
        {
            get
            {
                string lastUpload = _telemetryStatus.LastUploadedAt == null
                    ? "最近上传：未执行"
                    : $"最近上传：{FormatScheduleTime(_telemetryStatus.LastUploadedAt)}";
                return $"{lastUpload} · {_telemetryStatus.LastStatusText}";
            }
        }
        public string RecommendationHeadlineText => string.IsNullOrWhiteSpace(_recommendationSummary.Headline) ? "暂无智能建议" : _recommendationSummary.Headline;
        public string RecommendationDetailText => string.IsNullOrWhiteSpace(_recommendationSummary.Detail)
            ? "完成扫描后，这里会按你的空间结构、失败历史和偏好给出下一步建议。"
            : _recommendationSummary.Detail;
        public string RecommendationProfileText => string.IsNullOrWhiteSpace(_recommendationSummary.PreferenceModelText)
            ? "偏好模型：尚未建立"
            : _recommendationSummary.PreferenceModelText;
        public IReadOnlyList<CleanerRecommendationEntry> RecommendationEntries => _recommendationSummary.Entries;
        public string QualityGovernanceHeadlineText
        {
            get
            {
                CleanerRuleQualityEntry? topIssue = GetTopRuleIssue();
                if (topIssue == null)
                {
                    return HasLocallyDisabledRules
                        ? $"本地已停用 {_ruleStatus.LocalDisabledRuleCount} 条规则"
                        : "当前没有明显的规则事故信号";
                }

                return $"{topIssue.RuleName} 风险最高";
            }
        }
        public string QualityGovernanceDetailText
        {
            get
            {
                CleanerRuleQualityEntry? topIssue = GetTopRuleIssue();
                if (topIssue == null)
                {
                    return "这里会汇总失败率高、用户经常取消勾选的规则，方便做本地回滚和规则治理。";
                }

                string disabledText = topIssue.IsLocallyDisabled ? " · 已本地停用" : string.Empty;
                return $"{topIssue.SummaryText}{disabledText}";
            }
        }
        public string QualityGovernanceHintText => HasLocallyDisabledRules
            ? $"当前本地停用 {_ruleStatus.LocalDisabledRuleCount} 条规则，可随时恢复。"
            : "遇到持续失败或容易误判的规则，可以在这里导出诊断报告，或直接停用问题规则。";
        public string QualityGovernanceActionText => HasLocallyDisabledRules ? "恢复本地停用规则" : "当前没有本地停用规则";
        public string LatestCleanupSummaryText => _latestBatch?.SummaryText ?? "暂无最近一次清理记录";
        public string LatestCleanupHintText
        {
            get
            {
                if (_latestBatch == null)
                {
                    return "最近一次批次没有可恢复内容。";
                }

                if (_latestBatch.FailedCount > 0)
                {
                    string reasons = string.Join("、",
                        _latestBatch.Entries
                            .Where(entry => entry.FailureReason != CleanerFailureReason.None)
                            .GroupBy(entry => entry.FailureReason)
                            .OrderByDescending(group => group.Count())
                            .Select(group => CleanerPresentation.ToFailureReasonText(group.Key))
                            .Take(3));

                    return string.IsNullOrWhiteSpace(reasons)
                        ? $"最近一次有 {_latestBatch.FailedCount} 项失败。"
                        : $"最近一次失败原因主要是：{reasons}。";
                }

                return HasRestorableBatch ? "最近一次批次包含可恢复的隔离项。" : "最近一次批次没有可恢复内容。";
            }
        }
        public string ExclusionSummaryText => HasExclusions ? $"{Exclusions.Count} 条排除规则" : "暂无排除项";
        public string PrivilegeModeText => IsElevatedMode ? "管理员模式" : "标准模式";
        public string PrivilegeModeHintText => IsElevatedMode
            ? "当前已具备系统级目录扫描与清理权限，但仍受规则白名单边界限制。"
            : "当前只允许稳定处理用户级目录。系统级目录需要重新以管理员身份启动。";
        public string SystemBoundaryHintText => "系统级清理只允许命中规则声明的白名单根目录，例如 Windows Temp；即使已提权，也不会跨出这些边界。";
        public string FailureRecoveryElevateActionText => CanElevateAndRetryFailures ? "管理员模式并重试" : "进入管理员模式";
        public string FailureRecoveryHeadlineText
        {
            get
            {
                List<IGrouping<CleanerFailureReason, CleanerCleanupEntry>> groups = GetLatestFailedEntries()
                    .GroupBy(GetEffectiveFailureReason)
                    .OrderByDescending(group => group.Count())
                    .ToList();

                if (groups.Count == 0)
                {
                    return "最近一次没有需要处理的失败项。";
                }

                IGrouping<CleanerFailureReason, CleanerCleanupEntry> topGroup = groups[0];
                return $"最近一次失败 {GetLatestFailedEntries().Count} 项，其中 {CleanerPresentation.ToFailureReasonText(topGroup.Key)} {topGroup.Count()} 项。";
            }
        }
        public string FailureRecoveryDetailText
        {
            get
            {
                List<CleanerCleanupEntry> failedEntries = GetLatestFailedEntries();
                if (failedEntries.Count == 0)
                {
                    return "没有需要额外处理的失败项。";
                }

                List<string> suggestions = new();
                if (failedEntries.Any(entry => GetEffectiveFailureReason(entry) == CleanerFailureReason.InUse))
                {
                    suggestions.Add("先关闭占用程序，再执行“重试失败项”。");
                }

                if (failedEntries.Any(entry => GetEffectiveFailureReason(entry) == CleanerFailureReason.ElevationRequired))
                {
                    suggestions.Add(IsElevatedMode
                        ? "当前已经是管理员模式，可直接重试系统级失败项。"
                        : "有系统级目录需要管理员模式，提权后再重试。");
                }

                if (failedEntries.Any(entry => GetEffectiveFailureReason(entry) == CleanerFailureReason.AccessDenied))
                {
                    suggestions.Add(IsElevatedMode
                        ? "仍然提示权限不足时，建议检查原目录权限或交给系统自行清理。"
                        : "部分失败可能由权限不足导致，提权后可再试一次。");
                }

                if (failedEntries.Any(entry => GetEffectiveFailureReason(entry) == CleanerFailureReason.BoundaryBlocked))
                {
                    suggestions.Add("超出规则边界的对象会继续被阻止执行，这是保护机制，不建议绕过。");
                }

                if (failedEntries.Any(entry => GetEffectiveFailureReason(entry) == CleanerFailureReason.NotFound))
                {
                    suggestions.Add("对象已不存在的失败项无需再次处理。");
                }

                if (failedEntries.Any(entry => GetEffectiveFailureReason(entry) == CleanerFailureReason.ReparsePointSkipped))
                {
                    suggestions.Add("符号链接类对象默认跳过，避免跨目录误删。");
                }

                if (failedEntries.Any(entry => GetEffectiveFailureReason(entry) == CleanerFailureReason.Unknown))
                {
                    suggestions.Add("未知失败建议先重新扫描，再决定是否重试。");
                }

                return suggestions.Count == 0
                    ? "可以直接重试失败项；不可重试对象会继续保留。"
                    : string.Join(" ", suggestions.Take(3));
            }
        }
        public string FailureRecoveryProcessText
        {
            get
            {
                List<string> processes = GetFailureRecoveryProcesses();
                return processes.Count == 0 ? string.Empty : $"优先关闭：{string.Join(" / ", processes)}";
            }
        }
        public string AuditScanSummaryText => _auditSnapshot.ScanSummaryText;
        public string AuditCleanupSummaryText => _auditSnapshot.CleanupSummaryText;
        public string AuditRestoreSummaryText => _auditSnapshot.RestoreSummaryText;
        public string AuditRetrySummaryText => _auditSnapshot.RetrySummaryText;
        public string AuditUserChoiceSummaryText => _auditSnapshot.UserChoiceSummaryText;
        public string AuditFailureSummaryText => _auditSnapshot.TopFailureSummaryText;
        public bool HasScanTrend => _auditSnapshot.RecentScans.Count > 0;
        public bool HasScanTrendHistory => GetRecentScans().Count > 1;
        public string ScanTrendHeadlineText
        {
            get
            {
                CleanerScanSnapshot? latest = GetLatestScanSnapshot();
                if (latest == null)
                {
                    return "暂无扫描趋势";
                }

                return $"{latest.ScopeText} · {CleanerSizeFormatter.Format(latest.TotalBytes)}";
            }
        }
        public string ScanTrendDetailText
        {
            get
            {
                CleanerScanSnapshot? latest = GetLatestScanSnapshot();
                if (latest == null)
                {
                    return "完成首次扫描后，这里会显示最近几次扫描的空间变化。";
                }

                return $"{latest.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm} · 共 {latest.TotalItemCount} 项，其中可安全清理 {CleanerSizeFormatter.Format(latest.SafeBytes)}，建议确认 {CleanerSizeFormatter.Format(latest.ReviewBytes)}。";
            }
        }
        public string ScanTrendDeltaText
        {
            get
            {
                List<CleanerScanSnapshot> snapshots = GetRecentScans();
                if (snapshots.Count < 2)
                {
                    return "完成至少两次扫描后，会显示与上一次相比的空间变化。";
                }

                long delta = snapshots[0].TotalBytes - snapshots[1].TotalBytes;
                if (delta == 0)
                {
                    return "与上一次扫描相比，候选空间没有明显变化。";
                }

                string direction = delta > 0 ? "增加" : "减少";
                return $"比上一次扫描{direction} {CleanerSizeFormatter.Format(Math.Abs(delta))}";
            }
        }
        public string ScanTrendScopeText
        {
            get
            {
                CleanerScanSnapshot? latest = GetLatestScanSnapshot();
                if (latest == null)
                {
                    return "分析范围：尚未建立";
                }

                return $"分析范围：{latest.DriveSummaryText}";
            }
        }
        public string ScanTrendReuseText
        {
            get
            {
                CleanerScanSnapshot? latest = GetLatestScanSnapshot();
                if (latest == null)
                {
                    return "深度扫描会优先复用最近的快速扫描结果，避免重复扫同一批低风险目录。";
                }

                if (latest.UsedIncrementalReuse && latest.ReusedItemCount > 0)
                {
                    return $"本次扫描复用了最近快速扫描结果 {latest.ReusedItemCount} 项。";
                }

                return latest.Scope == CleanerScanScope.Deep
                    ? "本次深度扫描没有命中可复用的快速扫描结果，因此执行了完整扫描。"
                    : "最近一次是快速扫描；下一次深度扫描会优先尝试复用这一批结果。";
            }
        }
        public string ScanTrendCompositionText
        {
            get
            {
                CleanerScanSnapshot? latest = GetLatestScanSnapshot();
                if (latest == null)
                {
                    return "趋势构成：完成扫描后会显示安全清理、建议确认和仅供查看的分布。";
                }

                return $"构成：安全 {CleanerSizeFormatter.Format(latest.SafeBytes)} · 确认 {CleanerSizeFormatter.Format(latest.ReviewBytes)} · 查看 {CleanerSizeFormatter.Format(latest.ViewOnlyBytes)}";
            }
        }
        public string ScanTrendWindowText
        {
            get
            {
                List<CleanerScanSnapshot> snapshots = GetRecentScans();
                if (snapshots.Count == 0)
                {
                    return "趋势窗口：暂无历史";
                }

                DateTimeOffset earliest = snapshots[^1].CreatedAt;
                DateTimeOffset latest = snapshots[0].CreatedAt;
                return $"趋势窗口：{earliest.LocalDateTime:MM-dd HH:mm} 至 {latest.LocalDateTime:MM-dd HH:mm} · 最近 {snapshots.Count} 次扫描";
            }
        }
        public IReadOnlyList<CleanerScanTrendEntry> ScanTrendEntries => BuildScanTrendEntries();

        public CleanerAssistantViewModel(
            CleanerScanService scanService,
            CleanerExecutionService executionService,
            CleanerStateStore stateStore,
            CleanerRuleService ruleService,
            NativeFileService nativeFileService,
            CleanerPrivilegeService privilegeService,
            CleanerAuditService auditService,
            CleanerAutomationService automationService,
            CleanerLaunchActionService launchActionService,
            CleanerDriveService driveService,
            CleanerDeepScanService deepScanService,
            CleanerProfileService profileService,
            CleanerTelemetryService telemetryService,
            CleanerRecommendationService recommendationService,
            CleanerSettingsViewModel settings)
        {
            _scanService = scanService;
            _executionService = executionService;
            _stateStore = stateStore;
            _ruleService = ruleService;
            _nativeFileService = nativeFileService;
            _privilegeService = privilegeService;
            _auditService = auditService;
            _automationService = automationService;
            _launchActionService = launchActionService;
            _driveService = driveService;
            _deepScanService = deepScanService;
            _profileService = profileService;
            _telemetryService = telemetryService;
            _recommendationService = recommendationService;
            Settings = settings;

            SafeItems.CollectionChanged += OnCollectionChanged;
            ReviewItems.CollectionChanged += OnCollectionChanged;
            ViewOnlyItems.CollectionChanged += OnCollectionChanged;
            Exclusions.CollectionChanged += OnCollectionChanged;
            LatestCleanupEntries.CollectionChanged += OnCollectionChanged;
            DriveOptions.CollectionChanged += OnCollectionChanged;
        }

        public async Task InitializeAsync(ICleanerAssistantViewInteraction view, DispatcherQueue dispatcherQueue)
        {
            _view = view;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // Register for AI triggers
            WeakReferenceMessenger.Default.Unregister<StartQuickScanMessage>(this);
            WeakReferenceMessenger.Default.Register<StartQuickScanMessage>(this, async (r, m) => 
            {
                if (!IsBusy)
                {
                    await StartQuickScan();
                }
            });

            await ReloadPersistentStateAsync();
            await HandleLaunchActionsAsync();
            await HandleAutomationAsync();
        }

        [RelayCommand]
        private async Task StartQuickScan()
        {
            await StartScanAsync(CleanerScanScope.Quick);
        }

        [RelayCommand]
        private async Task StartDeepScan()
        {
            await StartScanAsync(CleanerScanScope.Deep);
        }

        [RelayCommand]
        private Task SetDailyReminder()
        {
            return SetReminderIntervalCoreAsync(1);
        }

        [RelayCommand]
        private Task Set3DayReminder()
        {
            return SetReminderIntervalCoreAsync(3);
        }

        [RelayCommand]
        private Task Set7DayReminder()
        {
            return SetReminderIntervalCoreAsync(7);
        }

        [RelayCommand]
        private Task Set14DayReminder()
        {
            return SetReminderIntervalCoreAsync(14);
        }

        [RelayCommand]
        private async Task RunAutomaticLowRiskCleanupNow()
        {
            await ExecuteAutomaticLowRiskCleanupAsync("立即自动保洁", showCompletionTip: true);
        }

        [RelayCommand]
        private Task SelectAllDrives()
        {
            ApplyDriveSelection(option => true);
            return Task.CompletedTask;
        }

        [RelayCommand]
        private Task UseSystemDriveOnly()
        {
            ApplyDriveSelection(option => option.IsSystemDrive);
            return Task.CompletedTask;
        }

        [RelayCommand]
        private async Task SetStableRolloutChannel()
        {
            await SetRolloutChannelAsync("stable");
        }

        [RelayCommand]
        private async Task SetCanaryRolloutChannel()
        {
            await SetRolloutChannelAsync("canary");
        }

        [RelayCommand]
        private async Task SetInternalRolloutChannel()
        {
            await SetRolloutChannelAsync("internal");
        }

        [RelayCommand]
        private async Task ConfigureTelemetryEndpoint()
        {
            if (_view == null)
            {
                return;
            }

            string? endpoint = await _view.PromptTelemetryEndpointAsync(_telemetryStatus.Endpoint);
            if (endpoint == null)
            {
                return;
            }

            try
            {
                ApplyTelemetryStatus(await _telemetryService.SaveSettingsAsync(Settings.TelemetryEnabled, endpoint));
                RaiseDashboardProperties();
                await _view.ShowTipAsync("遥测地址已更新", string.IsNullOrWhiteSpace(endpoint) ? "已清空遥测上传地址。" : "新的遥测上传地址已保存。");
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync("保存失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task UploadTelemetryNow()
        {
            if (_view == null)
            {
                return;
            }

            CancellationTokenSource cts = CreateOperationCts();
            try
            {
                SetBusyState(true, "正在上传治理遥测", "准备上传规则质量、失败分类和趋势摘要。");
                ApplyTelemetryStatus(await _telemetryService.UploadNowAsync(cts.Token));
                RaiseDashboardProperties();
                await _view.ShowTipAsync("上传完成", "云端治理摘要已上传。");
            }
            catch (OperationCanceledException)
            {
                StatusMainText = "已取消";
                StatusDetailText = "遥测上传已被中止。";
            }
            catch (Exception ex)
            {
                ApplyTelemetryStatus(await _telemetryService.LoadStatusAsync());
                RaiseDashboardProperties();
                await _view.ShowTipAsync("上传失败", ex.Message);
            }
            finally
            {
                ReleaseOperationCts(cts);
                SetBusyState(false, StatusMainText, StatusDetailText);
            }
        }

        [RelayCommand]
        private async Task ImportRulePack()
        {
            if (_view == null)
            {
                return;
            }

            string? filePath = await _view.PickRulePackFileAsync();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                CleanerRuleBundleStatus status = await _ruleService.ImportRulePackAsync(filePath);
                _scanService.InvalidateIncrementalCache();
                await ReloadRuleStatusAsync();
                await _view.ShowTipAsync("规则包已导入", $"当前有效规则 {status.EffectiveRuleCount} 条。");
                await RefreshCurrentResultsIfNeededAsync();
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync("导入失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task RefreshRulePackFromUrl()
        {
            if (_view == null)
            {
                return;
            }

            string? remoteUri = await _view.PromptRulePackUrlAsync(_ruleStatus.RemoteUri);
            if (string.IsNullOrWhiteSpace(remoteUri))
            {
                return;
            }

            CancellationTokenSource cts = CreateOperationCts();
            try
            {
                SetBusyState(true, "正在刷新规则包", "正在从指定链接拉取外部规则和停用配置。");
                CleanerRuleBundleStatus status = await _ruleService.RefreshFromRemoteAsync(remoteUri, cts.Token);
                _scanService.InvalidateIncrementalCache();
                await ReloadRuleStatusAsync();
                await _view.ShowTipAsync("规则包已刷新", $"当前有效规则 {status.EffectiveRuleCount} 条。");
                await RefreshCurrentResultsIfNeededAsync();
            }
            catch (OperationCanceledException)
            {
                StatusMainText = "已取消";
                StatusDetailText = "规则刷新已被取消。";
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync("刷新失败", ex.Message);
            }
            finally
            {
                ReleaseOperationCts(cts);
                SetBusyState(false, StatusMainText, StatusDetailText);
            }
        }

        [RelayCommand]
        private async Task ClearExternalRulePack()
        {
            if (_view == null)
            {
                return;
            }

            if (!HasExternalRulePack)
            {
                await _view.ShowTipAsync("没有外部规则包", "当前只在使用内置规则库。");
                return;
            }

            CleanerRuleBundleStatus status = await _ruleService.ClearExternalRulePackAsync();
            _scanService.InvalidateIncrementalCache();
            await ReloadRuleStatusAsync();
            await _view.ShowTipAsync("已恢复内置规则", $"当前有效规则 {status.EffectiveRuleCount} 条。");
            await RefreshCurrentResultsIfNeededAsync();
        }

        [RelayCommand]
        private async Task OpenRulePackDirectory()
        {
            await _nativeFileService.OpenFolderAsync(_stateStore.RulePackDirectoryPath);
        }

        [RelayCommand]
        private async Task ExportDiagnosticReport()
        {
            if (_view == null)
            {
                return;
            }

            try
            {
                string reportPath = await _auditService.ExportDiagnosticReportAsync(_knownRules, _ruleStatus);
                await _nativeFileService.RevealInExplorerAsync(reportPath);
                await _view.ShowTipAsync("诊断报告已导出", $"已生成本地诊断报告：{Path.GetFileName(reportPath)}");
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync("导出失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task EnableAllLocallyDisabledRules()
        {
            if (_view == null || !HasLocallyDisabledRules)
            {
                return;
            }

            CleanerRuleBundleStatus status = await _ruleService.EnableAllLocallyDisabledRulesAsync();
            _scanService.InvalidateIncrementalCache();
            await ReloadRuleStatusAsync();
            await _view.ShowTipAsync("已恢复本地停用规则", $"当前有效规则 {status.EffectiveRuleCount} 条。");
            await RefreshCurrentResultsIfNeededAsync();
        }

        [RelayCommand]
        private async Task DisableRuleFromCleanupEntry(CleanerCleanupEntry? entry)
        {
            if (entry == null || _view == null || !entry.HasRuleId)
            {
                return;
            }

            string ruleName = GetRuleDisplayName(entry.RuleId);
            bool confirmed = await _view.ShowRuleDisableConfirmationAsync(ruleName, entry.RuleId);
            if (!confirmed)
            {
                return;
            }

            CleanerRuleBundleStatus status = await _ruleService.DisableRuleLocallyAsync(entry.RuleId);
            _scanService.InvalidateIncrementalCache();
            await ReloadRuleStatusAsync();
            await _view.ShowTipAsync("规则已本地停用", $"已停用规则：{ruleName}。当前有效规则 {status.EffectiveRuleCount} 条。");
            await RefreshCurrentResultsIfNeededAsync();
        }

        [RelayCommand]
        private void CancelCurrentOperation()
        {
            TryCancelOperation(_operationCts);
        }

        [RelayCommand]
        private async Task RunCleanup()
        {
            if (_view == null)
            {
                return;
            }

            List<CleanerScanItem> selectedItems = _allItems
                .Where(item => item.IsSelected && item.IsSelectableAndEnabled)
                .ToList();

            if (selectedItems.Count == 0)
            {
                await _view.ShowTipAsync("没有可执行项", "先勾选你希望纳入本次清理的对象。");
                return;
            }

            bool confirmed = await _view.ShowCleanupConfirmationAsync(
                selectedItems.Count,
                CleanerSizeFormatter.Format(selectedItems.Sum(item => item.SizeBytes)),
                selectedItems.Any(item => item.RiskLevel == CleanerRiskLevel.Medium));

            if (!confirmed)
            {
                return;
            }

            CancellationTokenSource cts = CreateOperationCts();
            try
            {
                SetBusyState(true, "正在执行清理", "优先使用隔离区和回收站，避免不可逆误删。");
                Progress<CleanerExecutionProgress> progress = new(UpdateExecutionProgress);
                CleanerCleanupBatch batch = await _executionService.ExecuteAsync(selectedItems, _lastScope, progress, cts.Token);
                _latestBatch = batch;
                await _auditService.RecordDeselectionAsync(_allItems);
                await _auditService.RecordCleanupAsync(batch, GetCurrentDeselectedDefaultCount());
                _auditSnapshot = await _auditService.LoadSnapshotAsync();
                _scanService.InvalidateIncrementalCache();
                RaiseDashboardProperties();

                string failureSummary = batch.FailedCount > 0
                    ? $"\n失败 {batch.FailedCount} 项，主要原因：{string.Join("、",
                        batch.Entries
                            .Where(entry => entry.FailureReason != CleanerFailureReason.None)
                            .GroupBy(entry => entry.FailureReason)
                            .OrderByDescending(group => group.Count())
                            .Select(group => CleanerPresentation.ToFailureReasonText(group.Key))
                            .Take(3))}"
                    : string.Empty;

                await _view.ShowTipAsync(
                    "清理完成",
                    $"本次释放 {CleanerSizeFormatter.Format(batch.ReleasedBytes)}，共处理 {batch.Entries.Count} 个对象。{failureSummary}");

                await StartScanAsync(_lastScope, silentIfBusy: true);
                await ReloadPersistentStateAsync();
            }
            catch (OperationCanceledException)
            {
                StatusMainText = "已取消";
                StatusDetailText = "当前操作已被中止。";
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync("清理失败", ex.Message);
            }
            finally
            {
                ReleaseOperationCts(cts);
                SetBusyState(false, StatusMainText, StatusDetailText);
            }
        }

        [RelayCommand]
        private async Task RestoreLatestCleanup()
        {
            if (_view == null)
            {
                return;
            }

            if (!HasRestorableBatch)
            {
                await _view.ShowTipAsync("没有可恢复项", "最近一次批次没有隔离区内容可供恢复。");
                return;
            }

            bool confirmed = await _view.ShowRestoreConfirmationAsync(LatestCleanupSummaryText);
            if (!confirmed)
            {
                return;
            }

            CancellationTokenSource cts = CreateOperationCts();
            try
            {
                SetBusyState(true, "正在恢复最近一次清理", "优先回写到原路径，遇到重名时自动改名保留。");
                CleanerRestoreSummary summary = await _executionService.RestoreLatestAsync(cts.Token);
                await _auditService.RecordRestoreAsync(summary);
                _scanService.InvalidateIncrementalCache();
                await ReloadPersistentStateAsync();
                await _view.ShowTipAsync("恢复结果", summary.Message);
                await StartScanAsync(_lastScope, silentIfBusy: true);
            }
            catch (OperationCanceledException)
            {
                StatusMainText = "已取消";
                StatusDetailText = "恢复操作已被中止。";
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync("恢复失败", ex.Message);
            }
            finally
            {
                ReleaseOperationCts(cts);
                SetBusyState(false, StatusMainText, StatusDetailText);
            }
        }

        [RelayCommand]
        private async Task AddToExclusions(CleanerScanItem? item)
        {
            if (item == null)
            {
                return;
            }

            List<CleanerExclusionEntry> current = (await _stateStore.LoadExclusionsAsync()).ToList();
            if (current.Any(entry => string.Equals(NormalizePath(entry.Path), NormalizePath(item.Path), StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            current.Add(new CleanerExclusionEntry
            {
                Path = item.Path,
                CreatedAt = DateTimeOffset.Now
            });

            await _stateStore.SaveExclusionsAsync(current);
            _scanService.InvalidateIncrementalCache();
            await ReloadPersistentStateAsync();
            await StartScanAsync(_lastScope, silentIfBusy: true);
        }

        [RelayCommand]
        private async Task RemoveExclusion(CleanerExclusionEntry? entry)
        {
            if (entry == null)
            {
                return;
            }

            List<CleanerExclusionEntry> current = (await _stateStore.LoadExclusionsAsync()).ToList();
            current.RemoveAll(item => string.Equals(NormalizePath(item.Path), NormalizePath(entry.Path), StringComparison.OrdinalIgnoreCase));
            await _stateStore.SaveExclusionsAsync(current);
            _scanService.InvalidateIncrementalCache();
            await ReloadPersistentStateAsync();
        }

        [RelayCommand]
        private async Task OpenItemLocation(CleanerScanItem? item)
        {
            if (item == null)
            {
                return;
            }

            if (File.Exists(item.Path))
            {
                await _nativeFileService.RevealInExplorerAsync(item.Path);
            }
            else
            {
                await _nativeFileService.OpenFolderAsync(item.Path);
            }
        }

        [RelayCommand]
        private async Task OpenCleanerWorkspace()
        {
            await _nativeFileService.OpenFolderAsync(_stateStore.RootPath);
        }

        [RelayCommand]
        private async Task OpenQuarantine()
        {
            await _nativeFileService.OpenFolderAsync(_stateStore.QuarantineRootPath);
        }

        [RelayCommand]
        private async Task EnterElevatedMode()
        {
            if (_view == null)
            {
                return;
            }

            if (IsElevatedMode)
            {
                await _view.ShowTipAsync("已在管理员模式", "当前实例已经具备系统级清理权限。");
                return;
            }

            bool started = await _privilegeService.RestartElevatedAsync("CleanerAssistant");
            if (!started)
            {
                await _view.ShowTipAsync("提权未完成", "没有进入管理员模式。你可以稍后再次尝试。");
            }
        }

        [RelayCommand]
        private async Task EnterElevatedModeAndRetryFailures()
        {
            if (_view == null)
            {
                return;
            }

            CleanerCleanupBatch? batch = _latestBatch;
            if (batch == null || !batch.Entries.Any(entry => entry.CanRetryEntry))
            {
                await _view.ShowTipAsync("没有可重试项", "最近一次批次中没有适合在提权后重试的失败项。");
                return;
            }

            bool started = await _privilegeService.RestartElevatedAsync(
                "CleanerAssistant",
                [$"--cleaner-retry-batch={batch.BatchId}"]);

            if (!started)
            {
                await _view.ShowTipAsync("提权未完成", "没有进入管理员模式，因此未自动重试失败项。");
            }
        }

        [RelayCommand]
        private async Task RetryFailedCleanupEntries()
        {
            await RetryFailedCleanupEntriesCoreAsync(_latestBatch?.BatchId, "重试完成");
        }

        [RelayCommand]
        private async Task RestoreCleanupEntry(CleanerCleanupEntry? entry)
        {
            if (entry == null || _latestBatch == null || _view == null)
            {
                return;
            }

            if (!entry.CanRestoreEntry)
            {
                await _view.ShowTipAsync("无法恢复", "该条目当前不可恢复，可能已经恢复过或不是隔离区条目。");
                return;
            }

            bool confirmed = await _view.ShowRestoreConfirmationAsync($"{entry.ItemName} · {entry.SizeText}");
            if (!confirmed)
            {
                return;
            }

            CancellationTokenSource cts = CreateOperationCts();
            try
            {
                SetBusyState(true, "正在恢复指定条目", "仅恢复你选中的隔离项目。");
                CleanerRestoreSummary summary = await _executionService.RestoreEntryAsync(_latestBatch.BatchId, entry.EntryId, cts.Token);
                await _auditService.RecordRestoreAsync(summary);
                _scanService.InvalidateIncrementalCache();
                await ReloadPersistentStateAsync();
                await _view.ShowTipAsync("恢复结果", summary.Message);
                await StartScanAsync(_lastScope, silentIfBusy: true);
            }
            catch (OperationCanceledException)
            {
                StatusMainText = "已取消";
                StatusDetailText = "恢复操作已被中止。";
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync("恢复失败", ex.Message);
            }
            finally
            {
                ReleaseOperationCts(cts);
                SetBusyState(false, StatusMainText, StatusDetailText);
            }
        }

        [RelayCommand]
        private async Task OpenCleanupEntryOriginalPath(CleanerCleanupEntry? entry)
        {
            if (entry == null)
            {
                return;
            }

            string targetPath = File.Exists(entry.OriginalPath) || Directory.Exists(entry.OriginalPath)
                ? entry.OriginalPath
                : Path.GetDirectoryName(entry.OriginalPath) ?? entry.OriginalPath;

            if (File.Exists(targetPath))
            {
                await _nativeFileService.RevealInExplorerAsync(targetPath);
            }
            else
            {
                await _nativeFileService.OpenFolderAsync(targetPath);
            }
        }

        [RelayCommand]
        private async Task OpenCleanupEntryBackupPath(CleanerCleanupEntry? entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.BackupPath))
            {
                return;
            }

            if (File.Exists(entry.BackupPath))
            {
                await _nativeFileService.RevealInExplorerAsync(entry.BackupPath);
            }
            else if (Directory.Exists(entry.BackupPath))
            {
                await _nativeFileService.OpenFolderAsync(entry.BackupPath);
            }
        }

        private async Task StartScanAsync(CleanerScanScope scope, bool silentIfBusy = false)
        {
            if (IsBusy && !silentIfBusy)
            {
                return;
            }

            CancellationTokenSource cts = CreateOperationCts();
            _lastScope = scope;
            CleanerScanOptions scanOptions = BuildScanOptions();
            CleanerDiagnosticsLogger.Trace("CleanerVM", $"开始{scope}扫描。");

            try
            {
                ScanModeText = scope == CleanerScanScope.Quick ? "当前模式：快速扫描" : "当前模式：深度扫描";
                SetBusyState(
                    true,
                    scope == CleanerScanScope.Quick ? "正在执行快速扫描" : "正在执行深度扫描",
                    scope == CleanerScanScope.Quick
                        ? "先给出第一屏可安全处理的空间。"
                        : "先完成稳定的规则深扫，再按结果决定后续处理。");

                Progress<CleanerScanProgress> progress = new(UpdateScanProgress);
                CleanerScanReport finalReport;
                CleanerScanAddOnResult spaceAnalysisResult = CleanerScanAddOnResult.None;
                CleanerScanAddOnResult orphanResult = CleanerScanAddOnResult.None;

                if (scope == CleanerScanScope.Deep)
                {
                    CleanerDeepScanResult deepResult = await _deepScanService.ScanAsync(scanOptions, progress, cts.Token);
                    finalReport = deepResult.Report;
                    spaceAnalysisResult = deepResult.SpaceAnalysis;
                    orphanResult = deepResult.OrphanResidue;
                }
                else
                {
                    finalReport = await _scanService.ScanAsync(scope, scanOptions, progress, cts.Token);
                }

                CleanerDiagnosticsLogger.Trace("CleanerVM", $"扫描服务返回，共 {finalReport.Items.Count} 项，准备回填页面。");
                ApplyScanReport(finalReport);
                CleanerDiagnosticsLogger.Trace("CleanerVM", $"页面回填完成：安全 {SafeItems.Count} / 确认 {ReviewItems.Count} / 查看 {ViewOnlyItems.Count}。");

                await _auditService.RecordScanAsync(finalReport);
                CleanerDiagnosticsLogger.Trace("CleanerVM", "扫描审计写入完成。");
                _auditSnapshot = await _auditService.LoadSnapshotAsync();
                LastScanText = finalReport.UsedIncrementalReuse && finalReport.ReusedItemCount > 0
                    ? $"最近扫描：{finalReport.CreatedAt:yyyy-MM-dd HH:mm:ss} · 复用 {finalReport.ReusedItemCount} 项"
                    : $"最近扫描：{finalReport.CreatedAt:yyyy-MM-dd HH:mm:ss}";
                StatusMainText = scope == CleanerScanScope.Quick ? "快速扫描完成" : "深度扫描完成（含抽样分析）";
                StatusDetailText = BuildScanCompletionText(finalReport, spaceAnalysisResult, orphanResult);
                CleanerDiagnosticsLogger.Trace("CleanerVM", "扫描状态收尾完成。");
            }
            catch (OperationCanceledException)
            {
                StatusMainText = "已取消";
                StatusDetailText = "扫描任务已被取消。";
                CleanerDiagnosticsLogger.Trace("CleanerVM", "扫描已取消。");
            }
            catch (Exception ex)
            {
                CleanerDiagnosticsLogger.Trace("CleanerVM", $"扫描异常：{ex.GetType().FullName} - {ex.Message}");
                if (_view != null)
                {
                    await _view.ShowTipAsync("扫描失败", ex.Message);
                }
            }
            finally
            {
                ReleaseOperationCts(cts);
                SetBusyState(false, StatusMainText, StatusDetailText);
            }
        }

        private void ApplyScanReport(CleanerScanReport report)
        {
            ReplaceScanItems(report.Items);
        }

        private string BuildScanCompletionText(
            CleanerScanReport report,
            CleanerScanAddOnResult spaceAnalysisResult,
            CleanerScanAddOnResult orphanResult)
        {
            string summary = report.UsedIncrementalReuse && report.ReusedItemCount > 0
                ? $"共识别 {_allItems.Count} 个候选对象，复用了最近快速扫描结果 {report.ReusedItemCount} 项。"
                : $"共识别 {_allItems.Count} 个候选对象。";

            if (report.Scope == CleanerScanScope.Deep)
            {
                summary += " 空间占用部分采用抽样分析，不代表全盘穷举。";
            }

            List<string> additions = new();
            if (spaceAnalysisResult.Attempted)
            {
                additions.Add(spaceAnalysisResult.WasSkipped
                    ? "抽样空间占用分析已跳过，不影响当前结果"
                    : spaceAnalysisResult.AddedCount > 0
                        ? $"另补充 {spaceAnalysisResult.AddedCount} 项抽样空间占用分析结果"
                        : "未发现需要额外提示的大目录或大文件");
            }

            if (orphanResult.Attempted)
            {
                additions.Add(orphanResult.WasSkipped
                    ? "卸载残留提示已跳过，不影响当前结果"
                    : orphanResult.AddedCount > 0
                        ? $"另补充识别 {orphanResult.AddedCount} 项疑似卸载残留提示"
                        : "未发现需要额外提示的疑似卸载残留");
            }

            return additions.Count == 0
                ? summary
                : $"{summary} {string.Join("；", additions)}。";
        }

        private void ReplaceScanItems(IEnumerable<CleanerScanItem> items)
        {
            using IDisposable _ = SuspendDashboardRefresh();
            CleanerDiagnosticsLogger.Trace("CleanerVM", "开始批量替换扫描结果集合。");
            DetachTrackedItems();

            HashSet<string> exclusions = GetExclusionLookup();
            foreach (CleanerScanItem item in items)
            {
                PrepareScanItem(item, exclusions);
                _allItems.Add(item);
            }

            RebuildResultBuckets();
            CleanerDiagnosticsLogger.Trace("CleanerVM", "批量替换扫描结果集合完成。");
        }

        private void DetachTrackedItems()
        {
            foreach (CleanerScanItem item in _allItems)
            {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            _allItems.Clear();
            SafeItems.Clear();
            ReviewItems.Clear();
            ViewOnlyItems.Clear();
        }

        private void PrepareScanItem(CleanerScanItem item, HashSet<string> exclusions)
        {
            item.PropertyChanged -= Item_PropertyChanged;
            item.IsExcluded = exclusions.Contains(NormalizePath(item.Path));
            item.IsSelected = item.IsSelectableAndEnabled && item.IsSelected;
            item.PropertyChanged += Item_PropertyChanged;
        }

        private void RebuildResultBuckets()
        {
            SafeItems.Clear();
            ReviewItems.Clear();
            ViewOnlyItems.Clear();
            int addedViewOnlyCount = 0;

            foreach (CleanerScanItem item in _allItems
                .OrderByDescending(candidate => candidate.SizeBytes)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (item.IsViewOnlyBucket)
                {
                    if (addedViewOnlyCount < MaxDisplayedViewOnlyItems)
                    {
                        ViewOnlyItems.Add(item);
                        addedViewOnlyCount++;
                    }
                }
                else if (item.IsReviewBucket)
                {
                    ReviewItems.Add(item);
                }
                else
                {
                    SafeItems.Add(item);
                }
            }

            RaiseDashboardProperties();
        }

        private HashSet<string> GetExclusionLookup()
        {
            return Exclusions
                .Select(entry => NormalizePath(entry.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private void UpdateScanProgress(CleanerScanProgress progress)
        {
            EnqueueUi(() =>
            {
                StatusMainText = progress.StageTitle;
                StatusDetailText = progress.Detail;
                ProgressValue = progress.ProgressValue;
                ProgressMax = progress.ProgressMax;
            });
        }

        private void UpdateExecutionProgress(CleanerExecutionProgress progress)
        {
            EnqueueUi(() =>
            {
                StatusMainText = progress.StageTitle;
                StatusDetailText = progress.Detail;
                ProgressValue = progress.ProgressValue;
                ProgressMax = progress.ProgressMax;
            });
        }

        private async Task ReloadPersistentStateAsync()
        {
            IReadOnlyList<CleanerExclusionEntry> exclusions = await _stateStore.LoadExclusionsAsync();
            IReadOnlyList<CleanerCleanupBatch> history = await _stateStore.LoadHistoryAsync();
            _auditSnapshot = await _auditService.LoadSnapshotAsync();
            await ReloadDriveOptionsAsync();
            await ReloadAutomationStatusAsync();
            await ReloadProfileAndTelemetryAsync();
            await ReloadRuleStatusAsync();

            Exclusions.Clear();
            foreach (CleanerExclusionEntry exclusion in exclusions.OrderByDescending(entry => entry.CreatedAt))
            {
                Exclusions.Add(exclusion);
            }

            _latestBatch = history.FirstOrDefault();
            LatestCleanupEntries.Clear();
            if (_latestBatch != null)
            {
                foreach (CleanerCleanupEntry entry in _latestBatch.Entries
                    .OrderByDescending(item => item.Restored)
                    .ThenByDescending(item => item.HasFailure)
                    .ThenByDescending(item => item.SizeBytes)
                    .Take(12))
                {
                    LatestCleanupEntries.Add(entry);
                }
            }

            RaiseDashboardProperties();
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CleanerScanItem.IsSelected) || e.PropertyName == nameof(CleanerScanItem.IsExcluded))
            {
                RaiseDashboardProperties();
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RaiseDashboardProperties();
        }

        private void RaiseDashboardProperties()
        {
            if (IsDashboardRefreshSuspended)
            {
                _dashboardRefreshPending = true;
                return;
            }

            RefreshRecommendationSummary();
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasSafeItems));
            OnPropertyChanged(nameof(HasReviewItems));
            OnPropertyChanged(nameof(HasViewOnlyItems));
            OnPropertyChanged(nameof(HasExclusions));
            OnPropertyChanged(nameof(HasRestorableBatch));
            OnPropertyChanged(nameof(HasLatestCleanupEntries));
            OnPropertyChanged(nameof(HasLatestCleanupFailures));
            OnPropertyChanged(nameof(HasDriveOptions));
            OnPropertyChanged(nameof(HasExternalRulePack));
            OnPropertyChanged(nameof(HasLocallyDisabledRules));
            OnPropertyChanged(nameof(CanRunCleanup));
            OnPropertyChanged(nameof(IsElevatedMode));
            OnPropertyChanged(nameof(CanEnterElevatedMode));
            OnPropertyChanged(nameof(HasRetryableFailures));
            OnPropertyChanged(nameof(CanElevateAndRetryFailures));
            OnPropertyChanged(nameof(HasFailureRecoveryProcesses));
            OnPropertyChanged(nameof(CanRunAutomaticLowRiskCleanupNow));
            OnPropertyChanged(nameof(IsStableRolloutSelected));
            OnPropertyChanged(nameof(IsCanaryRolloutSelected));
            OnPropertyChanged(nameof(IsInternalRolloutSelected));
            OnPropertyChanged(nameof(CanChooseStableRollout));
            OnPropertyChanged(nameof(CanChooseCanaryRollout));
            OnPropertyChanged(nameof(CanChooseInternalRollout));
            OnPropertyChanged(nameof(HasRecommendationEntries));
            OnPropertyChanged(nameof(CanUploadTelemetryNow));
            OnPropertyChanged(nameof(SafeSpaceText));
            OnPropertyChanged(nameof(ReviewSpaceText));
            OnPropertyChanged(nameof(ViewOnlySpaceText));
            OnPropertyChanged(nameof(HasHiddenViewOnlyItems));
            OnPropertyChanged(nameof(HiddenViewOnlyCount));
            OnPropertyChanged(nameof(ViewOnlyDisplayHintText));
            OnPropertyChanged(nameof(SelectedSummaryText));
            OnPropertyChanged(nameof(SelectedDriveSummaryText));
            OnPropertyChanged(nameof(DriveSelectionHintText));
            OnPropertyChanged(nameof(AutomationSummaryText));
            OnPropertyChanged(nameof(AutomationModeText));
            OnPropertyChanged(nameof(AutomationNextActionText));
            OnPropertyChanged(nameof(AutomationLastActionText));
            OnPropertyChanged(nameof(AutomationHintText));
            OnPropertyChanged(nameof(AutomationScheduleText));
            OnPropertyChanged(nameof(AutomationScheduleDetailText));
            OnPropertyChanged(nameof(RulePackSummaryText));
            OnPropertyChanged(nameof(RulePackDetailText));
            OnPropertyChanged(nameof(RulePackSourceText));
            OnPropertyChanged(nameof(RulePackRefreshText));
            OnPropertyChanged(nameof(RulePackRemoteUriText));
            OnPropertyChanged(nameof(RulePackHintText));
            OnPropertyChanged(nameof(RolloutSummaryText));
            OnPropertyChanged(nameof(RolloutDetailText));
            OnPropertyChanged(nameof(RolloutHintText));
            OnPropertyChanged(nameof(TelemetrySummaryText));
            OnPropertyChanged(nameof(TelemetryDetailText));
            OnPropertyChanged(nameof(TelemetryEndpointText));
            OnPropertyChanged(nameof(TelemetryLastUploadText));
            OnPropertyChanged(nameof(RecommendationHeadlineText));
            OnPropertyChanged(nameof(RecommendationDetailText));
            OnPropertyChanged(nameof(RecommendationProfileText));
            OnPropertyChanged(nameof(RecommendationEntries));
            OnPropertyChanged(nameof(QualityGovernanceHeadlineText));
            OnPropertyChanged(nameof(QualityGovernanceDetailText));
            OnPropertyChanged(nameof(QualityGovernanceHintText));
            OnPropertyChanged(nameof(QualityGovernanceActionText));
            OnPropertyChanged(nameof(SafeCountText));
            OnPropertyChanged(nameof(ReviewCountText));
            OnPropertyChanged(nameof(ViewOnlyCountText));
            OnPropertyChanged(nameof(LatestCleanupSummaryText));
            OnPropertyChanged(nameof(LatestCleanupHintText));
            OnPropertyChanged(nameof(ExclusionSummaryText));
            OnPropertyChanged(nameof(PrivilegeModeText));
            OnPropertyChanged(nameof(PrivilegeModeHintText));
            OnPropertyChanged(nameof(SystemBoundaryHintText));
            OnPropertyChanged(nameof(FailureRecoveryElevateActionText));
            OnPropertyChanged(nameof(FailureRecoveryHeadlineText));
            OnPropertyChanged(nameof(FailureRecoveryDetailText));
            OnPropertyChanged(nameof(FailureRecoveryProcessText));
            OnPropertyChanged(nameof(AuditScanSummaryText));
            OnPropertyChanged(nameof(AuditCleanupSummaryText));
            OnPropertyChanged(nameof(AuditRestoreSummaryText));
            OnPropertyChanged(nameof(AuditRetrySummaryText));
            OnPropertyChanged(nameof(AuditUserChoiceSummaryText));
            OnPropertyChanged(nameof(AuditFailureSummaryText));
            OnPropertyChanged(nameof(HasScanTrend));
            OnPropertyChanged(nameof(HasScanTrendHistory));
            OnPropertyChanged(nameof(ScanTrendHeadlineText));
            OnPropertyChanged(nameof(ScanTrendDetailText));
            OnPropertyChanged(nameof(ScanTrendDeltaText));
            OnPropertyChanged(nameof(ScanTrendScopeText));
            OnPropertyChanged(nameof(ScanTrendReuseText));
            OnPropertyChanged(nameof(ScanTrendCompositionText));
            OnPropertyChanged(nameof(ScanTrendWindowText));
            OnPropertyChanged(nameof(ScanTrendEntries));
        }

        private bool IsDashboardRefreshSuspended => Volatile.Read(ref _dashboardRefreshSuspendCount) > 0;

        private IDisposable SuspendDashboardRefresh()
        {
            Interlocked.Increment(ref _dashboardRefreshSuspendCount);
            return new DashboardRefreshScope(this);
        }

        private void ResumeDashboardRefresh()
        {
            if (Interlocked.Decrement(ref _dashboardRefreshSuspendCount) != 0)
            {
                return;
            }

            if (_dashboardRefreshPending)
            {
                _dashboardRefreshPending = false;
                RaiseDashboardProperties();
            }
        }

        private void RefreshRecommendationSummary()
        {
            _recommendationSummary = _recommendationService.BuildSummary(
                _auditSnapshot,
                _allItems,
                _profileState,
                _automationStatus,
                _ruleStatus);
        }

        private List<CleanerCleanupEntry> GetLatestFailedEntries()
        {
            return _latestBatch?.Entries
                .Where(entry => entry.HasFailure)
                .OrderByDescending(entry => entry.SizeBytes)
                .ToList() ?? new List<CleanerCleanupEntry>();
        }

        private List<string> GetFailureRecoveryProcesses()
        {
            return GetLatestFailedEntries()
                .Where(entry => GetEffectiveFailureReason(entry) == CleanerFailureReason.InUse)
                .SelectMany(entry => entry.LockedByProcesses)
                .Where(process => !string.IsNullOrWhiteSpace(process))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
        }

        private CleanerScanSnapshot? GetLatestScanSnapshot()
        {
            return GetRecentScans().FirstOrDefault();
        }

        private List<CleanerScanSnapshot> GetRecentScans()
        {
            return _auditSnapshot.RecentScans
                .OrderByDescending(item => item.CreatedAt)
                .Take(8)
                .ToList();
        }

        private IReadOnlyList<CleanerScanTrendEntry> BuildScanTrendEntries()
        {
            List<CleanerScanSnapshot> snapshots = GetRecentScans();
            List<CleanerScanTrendEntry> entries = new(snapshots.Count);

            for (int index = 0; index < snapshots.Count; index++)
            {
                CleanerScanSnapshot current = snapshots[index];
                CleanerScanSnapshot? previous = index + 1 < snapshots.Count ? snapshots[index + 1] : null;
                long delta = previous == null ? 0 : current.TotalBytes - previous.TotalBytes;

                string deltaText = previous == null
                    ? "作为趋势基线"
                    : delta == 0
                        ? "与上一次持平"
                        : delta > 0
                            ? $"较上次增加 {CleanerSizeFormatter.Format(delta)}"
                            : $"较上次减少 {CleanerSizeFormatter.Format(Math.Abs(delta))}";

                entries.Add(new CleanerScanTrendEntry
                {
                    CreatedAt = current.CreatedAt,
                    TimestampText = current.CreatedAt.LocalDateTime.ToString("MM-dd HH:mm"),
                    ScopeText = current.ScopeText,
                    TotalText = CleanerSizeFormatter.Format(current.TotalBytes),
                    DeltaText = deltaText,
                    CompositionText = $"安全 {CleanerSizeFormatter.Format(current.SafeBytes)} · 确认 {CleanerSizeFormatter.Format(current.ReviewBytes)} · 查看 {CleanerSizeFormatter.Format(current.ViewOnlyBytes)}",
                    DriveSummaryText = current.DriveSummaryText,
                    ReuseText = current.UsedIncrementalReuse && current.ReusedItemCount > 0
                        ? $"复用 {current.ReusedItemCount} 项"
                        : "完整扫描"
                });
            }

            return entries;
        }

        private IReadOnlyList<CleanerRuleQualityEntry> GetRuleQualityEntries()
        {
            return CleanerAuditService.BuildRuleQualityEntries(
                _auditSnapshot,
                _knownRules,
                _ruleUpdateState.LocalDisabledRuleIds);
        }

        private CleanerRuleQualityEntry? GetTopRuleIssue()
        {
            return GetRuleQualityEntries()
                .Where(entry => entry.FailureCount > 0 || entry.DeselectionCount > 0)
                .OrderByDescending(entry => entry.IssueScore)
                .ThenByDescending(entry => entry.FailureCount)
                .ThenBy(entry => entry.RuleName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private string GetRuleDisplayName(string ruleId)
        {
            CleanerRuleDefinition? rule = _knownRules.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, ruleId, StringComparison.OrdinalIgnoreCase));

            return rule == null || string.IsNullOrWhiteSpace(rule.Name)
                ? ruleId
                : rule.Name;
        }

        private async Task ReloadAutomationStatusAsync()
        {
            ApplyAutomationStatus(await _automationService.LoadStatusAsync());
        }

        private async Task ReloadProfileAndTelemetryAsync()
        {
            _profileState = await _profileService.GetProfileAsync();
            ApplyTelemetryStatus(await _telemetryService.LoadStatusAsync());
        }

        private void ApplyAutomationStatus(CleanerAutomationStatus status)
        {
            _automationStatus = status;
            Settings.UpdateFromAutomationStatus(status);
        }

        private void ApplyTelemetryStatus(CleanerTelemetryStatus status)
        {
            _telemetryStatus = status;
            Settings.UpdateFromTelemetryStatus(status);
        }

        private void OnAutomationSettingsChanged()
        {

            _ = PersistAutomationSettingsAsync();
            RaiseDashboardProperties();
        }

        private void OnTelemetrySettingsChanged()
        {

            _ = PersistTelemetrySettingsAsync();
            RaiseDashboardProperties();
        }

        private async Task PersistAutomationSettingsAsync()
        {
            try
            {
                _automationStatus = await _automationService.SaveSettingsAsync(
                    Settings.ReminderEnabled,
                    Settings.AutoLowRiskCleanupEnabled,
                    Settings.ReminderIntervalDays);
                await PersistDriveSelectionAsync();
                RaiseDashboardProperties();
            }
            catch (Exception ex)
            {
                if (_view != null)
                {
                    await _view.ShowTipAsync("保存定时保洁设置失败", ex.Message);
                }
            }
        }

        private async Task PersistTelemetrySettingsAsync()
        {
            try
            {
                ApplyTelemetryStatus(await _telemetryService.SaveSettingsAsync(Settings.TelemetryEnabled, _telemetryStatus.Endpoint));
                RaiseDashboardProperties();
            }
            catch (Exception ex)
            {
                ApplyTelemetryStatus(await _telemetryService.LoadStatusAsync());
                RaiseDashboardProperties();
                if (_view != null)
                {
                    await _view.ShowTipAsync("保存云端治理设置失败", ex.Message);
                }
            }
        }

        private static string FormatScheduleTime(DateTimeOffset? value)
        {
            return value?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "-";
        }

        private Task SetReminderIntervalCoreAsync(int days)
        {
            Settings.ReminderIntervalDays = days;
            return Task.CompletedTask;
        }

        public Task SetReminderIntervalAsync(int days)
        {
            return SetReminderIntervalCoreAsync(days);
        }

        private async Task ReloadDriveOptionsAsync()
        {
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            HashSet<string> selectedRoots = preferences.SelectedDriveRoots
                .Select(NormalizePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<CleanerDriveOption> availableDrives = _driveService.GetAvailableDrives();
            bool hasSavedSelection = selectedRoots.Count > 0;

            _isUpdatingDriveSelection = true;
            try
            {
                foreach (CleanerDriveOption drive in DriveOptions)
                {
                    drive.PropertyChanged -= DriveOption_PropertyChanged;
                }

                DriveOptions.Clear();
                foreach (CleanerDriveOption drive in availableDrives)
                {
                    bool shouldSelect = hasSavedSelection
                        ? selectedRoots.Contains(NormalizePath(drive.RootPath))
                        : drive.IsSystemDrive;

                    drive.IsSelected = shouldSelect;
                    drive.PropertyChanged += DriveOption_PropertyChanged;
                    DriveOptions.Add(drive);
                }

                if (DriveOptions.Count > 0 && DriveOptions.All(option => !option.IsSelected))
                {
                    CleanerDriveOption fallback = DriveOptions.FirstOrDefault(option => option.IsSystemDrive) ?? DriveOptions[0];
                    fallback.IsSelected = true;
                }
            }
            finally
            {
                _isUpdatingDriveSelection = false;
            }

            await PersistDriveSelectionAsync();
        }

        private async Task ReloadRuleStatusAsync()
        {
            _ruleStatus = await _ruleService.GetStatusAsync();
            _ruleUpdateState = await _ruleService.GetUpdateStateAsync();
            _knownRules = await _ruleService.GetKnownRulesAsync();
        }

        private async Task SetRolloutChannelAsync(string rolloutChannel)
        {
            if (_view == null)
            {
                return;
            }

            try
            {
                _profileState = await _profileService.SetRolloutChannelAsync(rolloutChannel);
                await ReloadRuleStatusAsync();
                ApplyTelemetryStatus(await _telemetryService.LoadStatusAsync());
                _scanService.InvalidateIncrementalCache();
                RaiseDashboardProperties();
                await RefreshCurrentResultsIfNeededAsync();
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync("切换灰度通道失败", ex.Message);
            }
        }

        private void ApplyDriveSelection(Func<CleanerDriveOption, bool> selector)
        {
            if (DriveOptions.Count == 0)
            {
                return;
            }

            _isUpdatingDriveSelection = true;
            try
            {
                foreach (CleanerDriveOption drive in DriveOptions)
                {
                    drive.IsSelected = selector(drive);
                }

                if (DriveOptions.All(option => !option.IsSelected))
                {
                    CleanerDriveOption fallback = DriveOptions.FirstOrDefault(option => option.IsSystemDrive) ?? DriveOptions[0];
                    fallback.IsSelected = true;
                }
            }
            finally
            {
                _isUpdatingDriveSelection = false;
            }

            _ = PersistDriveSelectionAsync();
            RaiseDashboardProperties();
        }

        private async Task HandleLaunchActionsAsync()
        {
            string? retryBatchId = _launchActionService.ConsumeRetryBatchId();
            if (string.IsNullOrWhiteSpace(retryBatchId) || _view == null)
            {
                return;
            }

            if (!IsElevatedMode)
            {
                await _view.ShowTipAsync("自动重试未执行", "当前不在管理员模式，无法接管系统级失败项重试。");
                return;
            }

            await RetryFailedCleanupEntriesCoreAsync(retryBatchId, "管理员模式已接管");
        }

        private async Task HandleAutomationAsync()
        {
            if (_view == null)
            {
                return;
            }

            CleanerAutomationStatus status = await _automationService.LoadStatusAsync();
            ApplyAutomationStatus(status);
            RaiseDashboardProperties();

            if (status.AutoLowRiskCleanupEnabled && status.IsAutoCleanupDue)
            {
                await ExecuteAutomaticLowRiskCleanupAsync("定时自动保洁", showCompletionTip: true);
                return;
            }

            if (status.ReminderEnabled && status.IsReminderDue)
            {
                _automationStatus = await _automationService.MarkReminderHandledAsync();
                RaiseDashboardProperties();
                await _view.ShowTipAsync(
                    "清理提醒",
                    $"已到你设置的 {Settings.ReminderIntervalDays} 天周期。建议先执行一次快速扫描，确认当前低风险缓存和日志占用。");
            }
        }

        private async Task RetryFailedCleanupEntriesCoreAsync(string? batchId, string completionTitle)
        {
            if (_view == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(batchId))
            {
                await _view.ShowTipAsync("没有可重试项", "最近一次批次中没有失败项可供重试。");
                return;
            }

            IReadOnlyList<CleanerCleanupBatch> history = await _stateStore.LoadHistoryAsync();
            CleanerCleanupBatch? sourceBatch = history.FirstOrDefault(candidate =>
                string.Equals(candidate.BatchId, batchId, StringComparison.OrdinalIgnoreCase));

            if (sourceBatch == null)
            {
                await _view.ShowTipAsync("没有找到批次", "指定的失败批次已不存在。");
                return;
            }

            HashSet<string> retryEntryIds = sourceBatch.Entries
                .Where(entry => entry.CanRetryEntry)
                .Select(entry => entry.EntryId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (retryEntryIds.Count == 0)
            {
                await _view.ShowTipAsync("没有可重试项", "该批次中没有适合再次执行的失败项。");
                return;
            }

            CancellationTokenSource cts = CreateOperationCts();
            try
            {
                SetBusyState(true, "正在重试失败项", "请先关闭占用进程；系统级失败项会按当前权限重新尝试。");
                Progress<CleanerExecutionProgress> progress = new(UpdateExecutionProgress);
                CleanerCleanupBatch? batch = await _executionService.RetryFailedEntriesAsync(batchId, progress, cts.Token);
                string resultMessage = "没有可重试项被重新处理。";

                if (batch != null)
                {
                    _scanService.InvalidateIncrementalCache();
                    List<CleanerCleanupEntry> attemptedEntries = batch.Entries
                        .Where(entry => retryEntryIds.Contains(entry.EntryId))
                        .ToList();

                    int retryAttempts = attemptedEntries.Count;
                    int recovered = attemptedEntries.Count(entry =>
                        string.Equals(entry.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                        entry.FailureReason == CleanerFailureReason.None);
                    int remainingFailed = attemptedEntries.Count(entry => entry.HasFailure);

                    await _auditService.RecordRetryAsync(retryAttempts, recovered, attemptedEntries.Where(entry => entry.HasFailure));

                    resultMessage = recovered > 0
                        ? $"已成功重试 {recovered} 项"
                        : "本次重试没有挽回失败项";

                    resultMessage += remainingFailed > 0
                        ? $"，仍有 {remainingFailed} 项失败。"
                        : "，本批失败项已全部处理完成。";
                }

                await ReloadPersistentStateAsync();
                await _view.ShowTipAsync(completionTitle, resultMessage);
                await StartScanAsync(_lastScope, silentIfBusy: true);
            }
            catch (OperationCanceledException)
            {
                StatusMainText = "已取消";
                StatusDetailText = "重试任务已被中止。";
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync("重试失败", ex.Message);
            }
            finally
            {
                ReleaseOperationCts(cts);
                SetBusyState(false, StatusMainText, StatusDetailText);
            }
        }

        private async Task ExecuteAutomaticLowRiskCleanupAsync(string title, bool showCompletionTip)
        {
            if (_view == null)
            {
                return;
            }

            CancellationTokenSource cts = CreateOperationCts();
            try
            {
                SetBusyState(true, title, "只会扫描并处理默认勾选的低风险项，不会触碰建议确认项或查看项。");
                Progress<CleanerScanProgress> scanProgress = new(UpdateScanProgress);
                CleanerScanReport report = await _scanService.ScanAsync(CleanerScanScope.Quick, BuildScanOptions(), scanProgress, cts.Token);
                ApplyScanReport(report);
                await _auditService.RecordScanAsync(report);
                _auditSnapshot = await _auditService.LoadSnapshotAsync();

                List<CleanerScanItem> scheduledItems = _allItems
                    .Where(item => item.IsSafeBucket && item.IsSelected && item.IsSelectableAndEnabled)
                    .ToList();

                if (scheduledItems.Count == 0)
                {
                    _automationStatus = await _automationService.MarkAutoCleanupHandledAsync();
                    ScanModeText = "当前模式：自动低风险清理";
                    LastScanText = $"最近扫描：{report.CreatedAt:yyyy-MM-dd HH:mm:ss}";
                    StatusMainText = $"{title}完成";
                    StatusDetailText = "本次没有发现符合自动保洁条件的低风险对象。";
                    RaiseDashboardProperties();

                    if (showCompletionTip)
                    {
                        await _view.ShowTipAsync(title, "本次没有发现符合自动保洁条件的低风险对象。");
                    }

                    return;
                }

                Progress<CleanerExecutionProgress> executionProgress = new(UpdateExecutionProgress);
                CleanerCleanupBatch batch = await _executionService.ExecuteAsync(
                    scheduledItems,
                    CleanerScanScope.Quick,
                    executionProgress,
                    cts.Token);

                _latestBatch = batch;
                await _auditService.RecordCleanupAsync(batch, manualDeselections: 0);
                _auditSnapshot = await _auditService.LoadSnapshotAsync();
                _automationStatus = await _automationService.MarkAutoCleanupHandledAsync();
                _scanService.InvalidateIncrementalCache();
                await ReloadPersistentStateAsync();
                RaiseDashboardProperties();

                if (showCompletionTip)
                {
                    string message = batch.ReleasedBytes > 0
                        ? $"已自动处理 {batch.CompletedCount} 项低风险对象，释放 {CleanerSizeFormatter.Format(batch.ReleasedBytes)}。"
                        : "已检查低风险对象，但这次没有释放出可见空间。";
                    await _view.ShowTipAsync(title, message);
                }

                await StartScanAsync(CleanerScanScope.Quick, silentIfBusy: true);
            }
            catch (OperationCanceledException)
            {
                StatusMainText = "已取消";
                StatusDetailText = "自动保洁已被中止。";
            }
            catch (Exception ex)
            {
                await _view.ShowTipAsync("自动保洁失败", ex.Message);
            }
            finally
            {
                ReleaseOperationCts(cts);
                SetBusyState(false, StatusMainText, StatusDetailText);
            }
        }

        private static CleanerFailureReason GetEffectiveFailureReason(CleanerCleanupEntry entry)
        {
            return entry.FailureReason == CleanerFailureReason.None && entry.HasFailure
                ? CleanerFailureReason.Unknown
                : entry.FailureReason;
        }

        private async void DriveOption_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CleanerDriveOption.IsSelected) || _isUpdatingDriveSelection)
            {
                return;
            }

            if (sender is CleanerDriveOption changedOption && DriveOptions.All(option => !option.IsSelected))
            {
                _isUpdatingDriveSelection = true;
                try
                {
                    changedOption.IsSelected = true;
                }
                finally
                {
                    _isUpdatingDriveSelection = false;
                }

                if (_view != null)
                {
                    await _view.ShowTipAsync("至少保留一个磁盘", "深度扫描的空间分析需要至少选中一个磁盘。");
                }
            }

            await PersistDriveSelectionAsync();
            RaiseDashboardProperties();
        }

        private CleanerScanOptions BuildScanOptions()
        {
            return new CleanerScanOptions
            {
                AnalysisDriveRoots = GetSelectedDriveOptions()
                    .Select(option => NormalizePath(option.RootPath))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        private List<CleanerDriveOption> GetSelectedDriveOptions()
        {
            return DriveOptions
                .Where(option => option.IsSelected)
                .OrderByDescending(option => option.IsSystemDrive)
                .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task PersistDriveSelectionAsync()
        {
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            preferences.SelectedDriveRoots = GetSelectedDriveOptions()
                .Select(option => NormalizePath(option.RootPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            await _stateStore.SavePreferencesAsync(preferences);
        }

        private async Task RefreshCurrentResultsIfNeededAsync()
        {
            if (_allItems.Count == 0)
            {
                return;
            }

            await StartScanAsync(_lastScope, silentIfBusy: true);
        }

        private int GetCurrentDeselectedDefaultCount()
        {
            return _allItems.Count(item =>
                item.DefaultSelected &&
                !item.IsSelected &&
                item.IsSelectableAndEnabled);
        }

        private CleanerPreferenceState BuildPreferenceState()
        {
            return new CleanerPreferenceState
            {
                SelectedDriveRoots = GetSelectedDriveOptions()
                    .Select(option => NormalizePath(option.RootPath))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ReminderEnabled = Settings.ReminderEnabled,
                AutoLowRiskCleanupEnabled = Settings.AutoLowRiskCleanupEnabled,
                ReminderIntervalDays = Settings.ReminderIntervalDays,
                LastReminderAt = _automationStatus.LastReminderAt,
                LastAutoCleanupAt = _automationStatus.LastAutoCleanupAt
            };
        }

        private static string FormatRolloutChannel(string rolloutChannel)
        {
            return CleanerProfileService.NormalizeChannel(rolloutChannel) switch
            {
                "canary" => "Canary",
                "internal" => "Internal",
                _ => "Stable"
            };
        }

        private CancellationTokenSource CreateOperationCts()
        {
            CancellationTokenSource next = new();
            CancellationTokenSource? previous = Interlocked.Exchange(ref _operationCts, next);
            TryCancelOperation(previous);
            return next;
        }

        private void ReleaseOperationCts(CancellationTokenSource cts)
        {
            if (ReferenceEquals(_operationCts, cts))
            {
                _operationCts = null;
            }

            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static void TryCancelOperation(CancellationTokenSource? cts)
        {
            if (cts == null)
            {
                return;
            }

            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void SetBusyState(bool busy, string mainText, string detailText)
        {
            EnqueueUi(() =>
            {
                IsBusy = busy;
                CanCancelCurrentOperation = busy;
                StatusMainText = mainText;
                StatusDetailText = detailText;
                if (!busy)
                {
                    ProgressValue = 0;
                    ProgressMax = 100;
                }
            });
        }

        private void EnqueueUi(Action action)
        {
            if (_dispatcherQueue == null)
            {
                action();
                return;
            }

            _dispatcherQueue.TryEnqueue(() => action());
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

        private sealed class DashboardRefreshScope : IDisposable
        {
            private CleanerAssistantViewModel? _owner;

            public DashboardRefreshScope(CleanerAssistantViewModel owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                CleanerAssistantViewModel? owner = Interlocked.Exchange(ref _owner, null);
                owner?.ResumeDashboardRefresh();
            }
        }

    }
}

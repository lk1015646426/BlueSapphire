using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.ViewModels.Cleaner
{
    public partial class CleanerScanViewModel : ObservableObject
    {
        private readonly CleanerScanService _scanService;
        private readonly CleanerDeepScanService _deepScanService;
        private readonly CleanerAuditService _auditService;
        private readonly CleanerStateStore _stateStore;
        private readonly AITaskCenterService? _taskCenter;
        private readonly AISharedContextService? _sharedContext;
        private readonly CleanerOperationCoordinator _operationCoordinator;
        private readonly SynchronizationContext? _uiContext;
        private bool _localOperationBusy;

        public CleanerDriveSelectionViewModel DriveSelection { get; }
        public CleanerCleanupViewModel Cleanup { get; }
        public CleanerSettingsViewModel Settings { get; }

        private ICleanerAssistantViewInteraction? _view;
        private readonly object _operationSync = new();
        private CancellationTokenSource? _currentOperationCts;
        private int _suspendRefreshCount;

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial CleanerScanState CurrentScanState { get; set; } = CleanerScanState.Idle;

        [ObservableProperty]
        public partial int ProgressValue { get; set; }

        partial void OnProgressValueChanged(int value)
        {
            OnPropertyChanged(nameof(ScanningProgressText));
        }

        [ObservableProperty]
        public partial string StatusMainText { get; set; } = "等待扫描";

        [ObservableProperty]
        public partial string StatusDetailText { get; set; } = "选择磁盘后开始扫描";

        [ObservableProperty]
        public partial string LastScanText { get; set; } = "最近扫描：无记录";

        private CleanerScanScope _lastScope = CleanerScanScope.Quick;

        public string ScanModeText => CurrentScanState == CleanerScanState.Completed
            ? (_lastScope == CleanerScanScope.Deep ? "深度扫描已完成" : "快速扫描已完成")
            : string.Empty;

        public bool HasScanCompleted => CurrentScanState == CleanerScanState.Completed;
        public bool HasNoResults => CurrentScanState == CleanerScanState.Completed && !HasResults;
        public bool ShowIdleEmptyState => CurrentScanState == CleanerScanState.Idle;
        public bool ShowNoResultsState => HasNoResults;
        public bool HasAnyCategoryItems => HasSafeItems || HasReviewItems || HasSystemItems || HasViewOnlyItems;
        public bool IsScanning => CurrentScanState == CleanerScanState.Scanning;
        // 所有磁盘操作共用忙碌门禁；清理、恢复或重试期间也不能启动新扫描。
        public bool IsNotScanning => !IsBusy;
        public bool IsIdle => CurrentScanState == CleanerScanState.Idle;

        public bool IsQuickScanMode => _lastScope == CleanerScanScope.Quick;
        public bool IsDeepScanMode => _lastScope == CleanerScanScope.Deep;

        public string ScanModeNameText => _lastScope == CleanerScanScope.Deep ? "深度扫描" : "快速扫描";

        public string ScanModeDescriptionText => _lastScope == CleanerScanScope.Deep
            ? "扩展扫描会检查更多应用与系统缓存，并对所选磁盘进行有上限的空间抽样；大文件和疑似残留只提供查看线索。"
            : "快速扫描会检查当前用户的临时文件、常用缓存、日志和开发工具缓存。";

        public string ScanModeEstimateText => _lastScope == CleanerScanScope.Deep
            ? "预计耗时 1-5 分钟"
            : "预计耗时 10-30 秒";

        public string ScanModeSafetyText => "清理前会再次确认，建议确认项默认不自动删除。";

        public string ScanningProgressText => IsScanning && ProgressMax > 0
            ? $"扫描中 {ProgressValue}%"
            : string.Empty;

        public string CleanupDisabledReasonText => TotalSelectedItemCount == 0
            ? "请选择清理项"
            : string.Empty;

        public string BottomRiskHintText => HasSelectedReviewItems
            ? $"包含 {SelectedReviewItemCount + SelectedSystemItemCount} 项需确认，执行前请核对"
            : (TotalSelectedItemCount > 0 ? "全部为低风险项，执行前仍可逐项核对" : string.Empty);

        public CleanerAuditSnapshot? AuditSnapshot { get; private set; }

        [ObservableProperty]
        public partial ObservableCollection<CleanerScanItem> SafeItems { get; set; } = new();

        [ObservableProperty]
        public partial ObservableCollection<CleanerScanItem> ReviewItems { get; set; } = new();

        [ObservableProperty]
        public partial ObservableCollection<CleanerScanItem> ViewOnlyItems { get; set; } = new();

        [ObservableProperty]
        public partial ObservableCollection<CleanerScanItem> SystemItems { get; set; } = new();
        private readonly List<CleanerScanItem> _allItems = new();

        public IReadOnlyList<CleanerScanItem> AllItems => _allItems;

        public int SafeItemCount => SafeItems.Count;
        public long SafeItemSpaceBytes => SafeItems.Sum(i => i.SizeBytes);
        public string SafeSpaceText => CleanerSizeFormatter.Format(SafeItemSpaceBytes);
        public string SafeCountText => $"{SafeItemCount} 项";
        public bool HasSafeItems => HasScanCompleted && SafeItems.Count > 0;

        public int ReviewItemCount => ReviewItems.Count;
        public long ReviewItemSpaceBytes => ReviewItems.Sum(i => i.SizeBytes);
        public string ReviewSpaceText => CleanerSizeFormatter.Format(ReviewItemSpaceBytes);
        public string ReviewCountText => $"{ReviewItemCount} 项";
        public bool HasReviewItems => HasScanCompleted && ReviewItems.Count > 0;

        public int ViewOnlyItemCount => ViewOnlyItems.Count;
        public long ViewOnlyItemSpaceBytes => ViewOnlyItems.Sum(i => i.SizeBytes);
        public string ViewOnlySpaceText => CleanerSizeFormatter.Format(ViewOnlyItemSpaceBytes);
        public string ViewOnlyCountText => $"{ViewOnlyItemCount} 项";
        public bool HasViewOnlyItems => HasScanCompleted && ViewOnlyItems.Count > 0;

        public int SystemItemCount => SystemItems.Count;
        public long SystemItemSpaceBytes => SystemItems.Sum(i => i.SizeBytes);
        public string SystemSpaceText => CleanerSizeFormatter.Format(SystemItemSpaceBytes);
        public string SystemCountText => $"{SystemItemCount} 项";
        public bool HasSystemItems => HasScanCompleted && SystemItems.Count > 0;

        public long TotalCleanableSpaceBytes => SafeItemSpaceBytes + ReviewItemSpaceBytes + SystemItemSpaceBytes;
        public string TotalCleanableSpaceText => CleanerSizeFormatter.Format(TotalCleanableSpaceBytes);
        public long TotalDetectedSpaceBytes => TotalCleanableSpaceBytes + ViewOnlyItemSpaceBytes;
        public string TotalDetectedSpaceText => CleanerSizeFormatter.Format(TotalDetectedSpaceBytes);
        public double SafeSpaceRatio => TotalDetectedSpaceBytes > 0 ? (double)SafeItemSpaceBytes / TotalDetectedSpaceBytes : 0;
        public double ReviewSpaceRatio => TotalDetectedSpaceBytes > 0 ? (double)(ReviewItemSpaceBytes + SystemItemSpaceBytes) / TotalDetectedSpaceBytes : 0;
        public double ViewOnlySpaceRatio => TotalDetectedSpaceBytes > 0 ? (double)ViewOnlyItemSpaceBytes / TotalDetectedSpaceBytes : 0;
        public double SafePlusReviewRatio => SafeSpaceRatio + ReviewSpaceRatio;

        public bool HasResults => _allItems.Count > 0;
        public int ProgressMax => 100;
        public bool CanCancelCurrentOperation => IsBusy;

        public string SelectedSummaryText => CleanerSizeFormatter.BuildSelectionSummary(_allItems);

        public int SelectedSafeItemCount => SafeItems.Count(i => i.IsSelected);
        public long SelectedSafeItemSpaceBytes => SafeItems.Where(i => i.IsSelected).Sum(i => i.SizeBytes);
        public string SelectedSafeSpaceText => CleanerSizeFormatter.Format(SelectedSafeItemSpaceBytes);

        public int SelectedReviewItemCount => ReviewItems.Count(i => i.IsSelected);
        public long SelectedReviewItemSpaceBytes => ReviewItems.Where(i => i.IsSelected).Sum(i => i.SizeBytes);
        public string SelectedReviewSpaceText => CleanerSizeFormatter.Format(SelectedReviewItemSpaceBytes);

        public int SelectedSystemItemCount => SystemItems.Count(i => i.IsSelected);
        public long SelectedSystemItemSpaceBytes => SystemItems.Where(i => i.IsSelected).Sum(i => i.SizeBytes);
        public string SelectedSystemSpaceText => $"系统处理 {CleanerSizeFormatter.Format(SelectedSystemItemSpaceBytes)}";
        public bool HasSelectedSystemItems => SelectedSystemItemCount > 0;

        public int DeselectedDefaultItemCount => SafeItems.Count(i => i.RiskLevel == CleanerRiskLevel.Low && !i.IsSelected) +
                                                 ReviewItems.Count(i => i.RiskLevel == CleanerRiskLevel.Low && !i.IsSelected);

        public int TotalSelectedItemCount => SelectedSafeItemCount + SelectedReviewItemCount + SelectedSystemItemCount;
        public long TotalSelectedSpaceBytes => SelectedSafeItemSpaceBytes + SelectedReviewItemSpaceBytes + SelectedSystemItemSpaceBytes;
        public string TotalSelectedSpaceText => CleanerSizeFormatter.Format(TotalSelectedSpaceBytes);
        public string TotalSelectedItemCountText => $"{TotalSelectedItemCount} 项";
        public long SelectedPermanentSpaceBytes => _allItems
            .Where(item => item.IsSelected && item.ExecutionMode == CleanerExecutionMode.Permanent)
            .Sum(item => item.SizeBytes);
        public string SelectedPermanentSpaceText => $"立即释放 {CleanerSizeFormatter.Format(SelectedPermanentSpaceBytes)}";
        public bool HasSelectedPermanentItems => SelectedPermanentSpaceBytes > 0;
        public long SelectedRecoverableSpaceBytes => _allItems
            .Where(item => item.IsSelected && item.ExecutionMode is CleanerExecutionMode.Quarantine or CleanerExecutionMode.Recycle)
            .Sum(item => item.SizeBytes);
        public string SelectedRecoverableSpaceText => $"可恢复暂存 {CleanerSizeFormatter.Format(SelectedRecoverableSpaceBytes)}";
        public bool HasSelectedRecoverableItems => SelectedRecoverableSpaceBytes > 0;

        public bool HasSelectedReviewItems => SelectedReviewItemCount + SelectedSystemItemCount > 0;
        public bool ShowSafeHint => TotalSelectedItemCount > 0 && !HasSelectedReviewItems;

        public bool CanRunCleanup => TotalSelectedItemCount > 0 && !IsBusy;
        public bool CanRunAutomaticLowRiskCleanupNow => !IsBusy;

        public event EventHandler? DashboardChanged;
        public event EventHandler<CleanerScanScope>? ScanStarted;
        public event EventHandler? ScanCompleted;

        public CleanerScanViewModel(
            CleanerScanService scanService,
            CleanerDeepScanService deepScanService,
            CleanerAuditService auditService,
            CleanerStateStore stateStore,
            CleanerDriveSelectionViewModel driveSelection,
            CleanerCleanupViewModel cleanup,
            CleanerSettingsViewModel settings,
            AITaskCenterService? taskCenter = null,
            AISharedContextService? sharedContext = null,
            CleanerOperationCoordinator? operationCoordinator = null)
        {
            _scanService = scanService;
            _deepScanService = deepScanService;
            _auditService = auditService;
            _stateStore = stateStore;
            DriveSelection = driveSelection;
            Cleanup = cleanup;
            Settings = settings;
            _taskCenter = taskCenter;
            _sharedContext = sharedContext;
            _operationCoordinator = operationCoordinator ?? new CleanerOperationCoordinator();
            _uiContext = SynchronizationContext.Current;
            _operationCoordinator.StateChanged += OperationCoordinator_StateChanged;
            IsBusy = _operationCoordinator.IsBusy;
            if (_sharedContext != null)
            {
                _sharedContext.CleanerScanChanged += SharedContext_CleanerScanChanged;
            }

        }

        public async Task InitializeAsync(ICleanerAssistantViewInteraction view)
        {
            _view = view;
            AuditSnapshot = await _auditService.LoadSnapshotAsync();
            CleanerScanReport? sharedScan = _sharedContext?.GetCleanerScan(TimeSpan.FromMinutes(30));
            if (!IsBusy && !HasResults && sharedScan != null)
            {
                ApplySharedScan(sharedScan);
            }
        }

        [RelayCommand]
        private async Task StartQuickScan()
        {
            await RunScanAsync(CleanerScanScope.Quick);
        }

        [RelayCommand]
        private async Task StartDeepScan()
        {
            await RunScanAsync(CleanerScanScope.Deep);
        }

        [RelayCommand]
        private void CancelCurrentOperation()
        {
            lock (_operationSync)
            {
                if (_currentOperationCts != null && !_currentOperationCts.IsCancellationRequested)
                {
                    StatusDetailText = "正在取消操作...";
                    _currentOperationCts.Cancel();
                }
            }
        }

        public async Task<bool> RunScanAsync(CleanerScanScope scope)
        {
            if (IsBusy)
            {
                if (_operationCoordinator.IsBusy)
                {
                    StatusMainText = "暂时无法开始扫描";
                    StatusDetailText = "清理助手正在执行另一项磁盘任务，请稍后再试。";
                }
                return false;
            }
            if (!_operationCoordinator.TryAcquire(CleanerOperationKind.Scan, out CleanerOperationLease? lease))
            {
                StatusMainText = "暂时无法开始扫描";
                StatusDetailText = "清理助手正在执行另一项磁盘任务，请稍后再试。";
                return false;
            }

            using (lease)
            {
                return await RunScanCoreAsync(scope);
            }
        }

        internal async Task<bool> RunScanWithinOperationAsync(
            CleanerScanScope scope,
            CleanerOperationLease operationLease)
        {
            if (!_operationCoordinator.Owns(operationLease))
            {
                throw new InvalidOperationException("扫描必须使用当前清理操作租约。 ");
            }

            return await RunScanCoreAsync(scope);
        }

        private async Task<bool> RunScanCoreAsync(CleanerScanScope scope)
        {
            bool hadPreviousResults = CurrentScanState == CleanerScanState.Completed;
            CleanerScanScope previousScope = _lastScope;
            ScanStarted?.Invoke(this, scope);
            _lastScope = scope;
            CurrentScanState = CleanerScanState.Scanning;
            SetBusyState(true, scope == CleanerScanScope.Quick ? "正在快速扫描" : "正在深度扫描", "正在初始化扫描规则");

            CancellationTokenSource cts = CreateOperationTokenSource();
            string driveKey = string.Join(
                "|",
                DriveSelection.DriveOptions
                    .Where(drive => drive.IsSelected)
                    .Select(drive => drive.RootPath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            using AITaskLease? task = _taskCenter?.Begin(
                "cleaner.scan",
                scope == CleanerScanScope.Deep ? "深度扫描" : "快速扫描",
                $"由清理工具发起，范围：{(driveKey.Length == 0 ? "默认磁盘" : driveKey)}",
                $"cleaner.scan:{scope}:{driveKey}");
            if (task?.IsDuplicate == true)
            {
                ReleaseOperationCts(cts);
                _lastScope = hadPreviousResults ? previousScope : scope;
                CurrentScanState = hadPreviousResults ? CleanerScanState.Completed : CleanerScanState.Idle;
                SetBusyState(false, "扫描正在运行", "相同范围的扫描任务已经在后台执行。");
                return false;
            }
            using CancellationTokenSource linkedCts = task == null
                ? CancellationTokenSource.CreateLinkedTokenSource(cts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(cts.Token, task.Token);

            var progress = new Progress<CleanerScanProgress>(e =>
            {
                if (linkedCts.IsCancellationRequested)
                {
                    return;
                }

                int nextProgress = e.ProgressMax > 0
                    ? Math.Clamp((int)Math.Round(e.ProgressValue / e.ProgressMax * 100), 0, 100)
                    : 0;
                // 规则检查完成后仍需构建指纹和整理结果；完成前不显示误导性的 100%。
                nextProgress = Math.Min(nextProgress, 95);
                ProgressValue = Math.Max(ProgressValue, nextProgress);
                if (!string.IsNullOrWhiteSpace(e.StageTitle))
                {
                    StatusMainText = e.StageTitle;
                }
                if (!string.IsNullOrWhiteSpace(e.Detail))
                {
                    StatusDetailText = e.Detail;
                }
                if (task != null)
                {
                    _taskCenter?.Report(
                        task.TaskId,
                        ProgressValue,
                        StatusMainText,
                        StatusDetailText);
                }
            });

            try
            {
                CleanerScanReport finalReport;
                CleanerScanAddOnResult? spaceAnalysisResult = null;
                CleanerScanAddOnResult? orphanResult = null;

                CleanerScanOptions options = new CleanerScanOptions
                {
                    AnalysisDriveRoots = DriveSelection.DriveOptions.Where(d => d.IsSelected).Select(d => d.RootPath).ToList(),
                    IncludeLargeObjectAnalysis = scope == CleanerScanScope.Deep,
                    IncludeOrphanResidueAnalysis = scope == CleanerScanScope.Deep
                };

                if (scope == CleanerScanScope.Deep)
                {
                    var deepResult = await _deepScanService.ScanAsync(options, (IProgress<CleanerScanProgress>)progress, linkedCts.Token);
                    finalReport = deepResult.Report;
                    spaceAnalysisResult = deepResult.SpaceAnalysis;
                    orphanResult = deepResult.OrphanResidue;
                }
                else
                {
                    finalReport = await _scanService.ScanAsync(scope, options, (IProgress<CleanerScanProgress>)progress, linkedCts.Token);
                }

                ReplaceScanItems(finalReport.Items);
                _sharedContext?.SetCleanerScan(finalReport);

                await _auditService.RecordScanAsync(finalReport);
                AuditSnapshot = await _auditService.LoadSnapshotAsync();

                LastScanText = finalReport.UsedIncrementalReuse && finalReport.ReusedItemCount > 0
                    ? $"最近扫描：{finalReport.CreatedAt:yyyy-MM-dd HH:mm:ss} · 复用 {finalReport.ReusedItemCount} 项"
                    : $"最近扫描：{finalReport.CreatedAt:yyyy-MM-dd HH:mm:ss}";

                StatusMainText = scope == CleanerScanScope.Quick ? "快速扫描完成" : "深度扫描完成";
                StatusDetailText = TotalCleanableSpaceBytes > 0
                    ? $"发现 {TotalCleanableSpaceText} 可清理空间"
                    : "未发现可清理项目，当前系统状态良好";
                CurrentScanState = CleanerScanState.Completed;
                if (task != null)
                {
                    _taskCenter?.Complete(
                        task.TaskId,
                        $"扫描完成，发现 {TotalCleanableSpaceText} 可清理空间。");
                }

                ScanCompleted?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (OperationCanceledException)
            {
                _lastScope = hadPreviousResults ? previousScope : scope;
                StatusMainText = "已取消";
                StatusDetailText = hadPreviousResults
                    ? "本次扫描已取消，仍显示上一次完整扫描结果。"
                    : "扫描任务已被取消。";
                CurrentScanState = hadPreviousResults ? CleanerScanState.Completed : CleanerScanState.Idle;
                if (task != null)
                {
                    _taskCenter?.MarkCancelled(task.TaskId);
                }
                return false;
            }
            catch (Exception ex)
            {
                _lastScope = hadPreviousResults ? previousScope : scope;
                StatusMainText = "扫描失败";
                StatusDetailText = hadPreviousResults
                    ? "本次扫描未完成，仍显示上一次完整扫描结果。"
                    : "扫描未完成，请查看错误后重试。";
                CurrentScanState = hadPreviousResults ? CleanerScanState.Completed : CleanerScanState.Idle;
                if (task != null)
                {
                    _taskCenter?.Fail(task.TaskId, ex.Message);
                }
                if (_view != null)
                {
                    await _view.ShowTipAsync("扫描失败", ex.Message);
                }
                return false;
            }
            finally
            {
                ReleaseOperationCts(cts);
                SetBusyState(false, StatusMainText, StatusDetailText);
            }
        }

        private string BuildScanCompletionText(CleanerScanReport report, CleanerScanAddOnResult? spaceAnalysisResult, CleanerScanAddOnResult? orphanResult)
        {
            string summary = report.UsedIncrementalReuse && report.ReusedItemCount > 0
                ? $"共识别 {_allItems.Count} 个候选对象，复用了最近快速扫描结果 {report.ReusedItemCount} 项。"
                : $"共识别 {_allItems.Count} 个候选对象。";

            if (report.Scope == CleanerScanScope.Deep)
            {
                summary += " 空间占用部分采用抽样分析，不代表全盘穷举。";
            }

            List<string> additions = new();
            if (spaceAnalysisResult != null && spaceAnalysisResult.Attempted)
            {
                additions.Add(spaceAnalysisResult.WasSkipped
                    ? "抽样空间占用分析已跳过"
                    : spaceAnalysisResult.AddedCount > 0 ? $"补充 {spaceAnalysisResult.AddedCount} 项空间分析" : "未发现大文件");
            }

            if (orphanResult != null && orphanResult.Attempted)
            {
                additions.Add(orphanResult.WasSkipped
                    ? "卸载残留提示已跳过"
                    : orphanResult.AddedCount > 0 ? $"补充 {orphanResult.AddedCount} 项残留" : "未发现残留");
            }

            return additions.Count == 0 ? summary : $"{summary} {string.Join("；", additions)}。";
        }

        public void ReplaceScanItems(IEnumerable<CleanerScanItem> items)
        {
            using IDisposable _ = SuspendDashboardRefresh();

            foreach (CleanerScanItem item in _allItems)
            {
                item.PropertyChanged -= ScanItem_PropertyChanged;
            }

            _allItems.Clear();
            var newSafe = new ObservableCollection<CleanerScanItem>();
            var newReview = new ObservableCollection<CleanerScanItem>();
            var newViewOnly = new ObservableCollection<CleanerScanItem>();
            var newSystem = new ObservableCollection<CleanerScanItem>();

            HashSet<string> exclusions = Cleanup.GetExclusionLookup();

            foreach (CleanerScanItem item in items)
            {
                PrepareScanItem(item, exclusions);

                _allItems.Add(item);
                if (item.ExecutionMode == CleanerExecutionMode.System)
                {
                    newSystem.Add(item);
                    item.PropertyChanged += ScanItem_PropertyChanged;
                    continue;
                }
                switch (item.RiskLevel)
                {
                    case CleanerRiskLevel.Low:
                        newSafe.Add(item);
                        break;
                    case CleanerRiskLevel.Medium:
                        newReview.Add(item);
                        break;
                    case CleanerRiskLevel.High:
                        newViewOnly.Add(item);
                        break;
                }

                item.PropertyChanged += ScanItem_PropertyChanged;
            }

            SafeItems = newSafe;
            ReviewItems = newReview;
            ViewOnlyItems = newViewOnly;
            SystemItems = newSystem;
        }

        public void RefreshExclusionState()
        {
            using IDisposable _ = SuspendDashboardRefresh();
            HashSet<string> exclusions = Cleanup.GetExclusionLookup();

            foreach (CleanerScanItem item in _allItems)
            {
                bool wasExcluded = item.IsExcluded;
                bool isExcluded = exclusions.Contains(NormalizePath(item.Path));
                item.IsExcluded = isExcluded;

                if (!isExcluded && wasExcluded)
                {
                    item.IsSelected = item.DefaultSelected &&
                                      item.RiskLevel == CleanerRiskLevel.Low &&
                                      item.IsSelectableAndEnabled;
                }
            }
        }

        private void PrepareScanItem(CleanerScanItem item, HashSet<string> exclusions)
        {
            item.IsExcluded = exclusions.Contains(NormalizePath(item.Path));
            if (item.IsExcluded)
            {
                item.IsSelected = false;
            }
            else
            {
                item.IsSelected = item.DefaultSelected &&
                                  item.RiskLevel == CleanerRiskLevel.Low &&
                                  item.IsSelectableAndEnabled;
            }
        }

        private void ScanItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CleanerScanItem.IsSelected))
            {
                RaiseDashboardProperties();
            }
        }

        public IDisposable SuspendDashboardRefresh()
        {
            Interlocked.Increment(ref _suspendRefreshCount);
            return new ActionDisposable(() =>
            {
                if (Interlocked.Decrement(ref _suspendRefreshCount) == 0)
                {
                    RaiseDashboardProperties();
                }
            });
        }

        public void RaiseDashboardProperties()
        {
            if (_suspendRefreshCount > 0) return;

            OnPropertyChanged(nameof(SafeItemCount));
            OnPropertyChanged(nameof(SafeItemSpaceBytes));
            OnPropertyChanged(nameof(SafeSpaceText));
            OnPropertyChanged(nameof(SafeCountText));
            OnPropertyChanged(nameof(HasSafeItems));

            OnPropertyChanged(nameof(ReviewItemCount));
            OnPropertyChanged(nameof(ReviewItemSpaceBytes));
            OnPropertyChanged(nameof(ReviewSpaceText));
            OnPropertyChanged(nameof(ReviewCountText));
            OnPropertyChanged(nameof(HasReviewItems));

            OnPropertyChanged(nameof(ViewOnlyItemCount));
            OnPropertyChanged(nameof(ViewOnlyItemSpaceBytes));
            OnPropertyChanged(nameof(ViewOnlySpaceText));
            OnPropertyChanged(nameof(ViewOnlyCountText));
            OnPropertyChanged(nameof(HasViewOnlyItems));

            OnPropertyChanged(nameof(SystemItemCount));
            OnPropertyChanged(nameof(SystemItemSpaceBytes));
            OnPropertyChanged(nameof(SystemSpaceText));
            OnPropertyChanged(nameof(SystemCountText));
            OnPropertyChanged(nameof(HasSystemItems));

            OnPropertyChanged(nameof(SelectedSafeItemCount));
            OnPropertyChanged(nameof(SelectedSafeItemSpaceBytes));
            OnPropertyChanged(nameof(SelectedSafeSpaceText));

            OnPropertyChanged(nameof(SelectedReviewItemCount));
            OnPropertyChanged(nameof(SelectedReviewItemSpaceBytes));
            OnPropertyChanged(nameof(SelectedReviewSpaceText));

            OnPropertyChanged(nameof(SelectedSystemItemCount));
            OnPropertyChanged(nameof(SelectedSystemItemSpaceBytes));
            OnPropertyChanged(nameof(SelectedSystemSpaceText));
            OnPropertyChanged(nameof(HasSelectedSystemItems));

            OnPropertyChanged(nameof(DeselectedDefaultItemCount));
            OnPropertyChanged(nameof(TotalSelectedItemCount));
            OnPropertyChanged(nameof(TotalSelectedSpaceBytes));
            OnPropertyChanged(nameof(TotalSelectedSpaceText));
            OnPropertyChanged(nameof(TotalSelectedItemCountText));
            OnPropertyChanged(nameof(SelectedPermanentSpaceBytes));
            OnPropertyChanged(nameof(SelectedPermanentSpaceText));
            OnPropertyChanged(nameof(HasSelectedPermanentItems));
            OnPropertyChanged(nameof(SelectedRecoverableSpaceBytes));
            OnPropertyChanged(nameof(SelectedRecoverableSpaceText));
            OnPropertyChanged(nameof(HasSelectedRecoverableItems));

            OnPropertyChanged(nameof(HasSelectedReviewItems));
            OnPropertyChanged(nameof(ShowSafeHint));

            OnPropertyChanged(nameof(CanRunCleanup));
            OnPropertyChanged(nameof(CanRunAutomaticLowRiskCleanupNow));

            OnPropertyChanged(nameof(TotalCleanableSpaceBytes));
            OnPropertyChanged(nameof(TotalCleanableSpaceText));
            OnPropertyChanged(nameof(TotalDetectedSpaceBytes));
            OnPropertyChanged(nameof(TotalDetectedSpaceText));
            OnPropertyChanged(nameof(SafeSpaceRatio));
            OnPropertyChanged(nameof(ReviewSpaceRatio));
            OnPropertyChanged(nameof(ViewOnlySpaceRatio));
            OnPropertyChanged(nameof(SafePlusReviewRatio));

            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasScanCompleted));
            OnPropertyChanged(nameof(HasNoResults));
            OnPropertyChanged(nameof(ShowIdleEmptyState));
            OnPropertyChanged(nameof(ShowNoResultsState));
            OnPropertyChanged(nameof(HasAnyCategoryItems));
            OnPropertyChanged(nameof(ScanModeText));
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(IsNotScanning));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsQuickScanMode));
            OnPropertyChanged(nameof(IsDeepScanMode));
            OnPropertyChanged(nameof(ScanModeNameText));
            OnPropertyChanged(nameof(ScanModeDescriptionText));
            OnPropertyChanged(nameof(ScanModeEstimateText));
            OnPropertyChanged(nameof(ScanningProgressText));
            OnPropertyChanged(nameof(CleanupDisabledReasonText));
            OnPropertyChanged(nameof(BottomRiskHintText));

            DashboardChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetBusyState(bool isBusy, string mainText, string detailText)
        {
            _localOperationBusy = isBusy;
            IsBusy = _localOperationBusy || _operationCoordinator.IsBusy;
            StatusMainText = mainText;
            StatusDetailText = detailText;
            ProgressValue = isBusy ? 0 : IsBusy ? ProgressValue : 100;
            RaiseDashboardProperties();
        }

        private void OperationCoordinator_StateChanged(object? sender, EventArgs e)
        {
            if (_uiContext != null && SynchronizationContext.Current != _uiContext)
            {
                _uiContext.Post(_ => ApplyCoordinatorState(), null);
                return;
            }

            ApplyCoordinatorState();
        }

        private void ApplyCoordinatorState()
        {
            IsBusy = _localOperationBusy || _operationCoordinator.IsBusy;
            RaiseDashboardProperties();
        }

        public CancellationTokenSource CreateOperationTokenSource()
        {
            CancellationTokenSource cts = new();
            lock (_operationSync)
            {
                if (_currentOperationCts != null && !_currentOperationCts.IsCancellationRequested)
                {
                    _currentOperationCts.Cancel();
                }
                _currentOperationCts = cts;
            }
            return cts;
        }

        private void SharedContext_CleanerScanChanged(object? sender, CleanerScanReport report)
        {
            if (IsBusy)
            {
                return;
            }
            ApplySharedScan(report);
        }

        private void ApplySharedScan(CleanerScanReport report)
        {
            _lastScope = report.Scope;
            ReplaceScanItems(report.Items);
            LastScanText = $"最近扫描：{report.CreatedAt:yyyy-MM-dd HH:mm:ss} · 共享任务结果";
            StatusMainText = report.Scope == CleanerScanScope.Deep ? "深度扫描已完成" : "快速扫描已完成";
            StatusDetailText = TotalCleanableSpaceBytes > 0
                ? $"发现 {TotalCleanableSpaceText} 可清理空间"
                : "未发现可清理项目，当前系统状态良好";
            CurrentScanState = CleanerScanState.Completed;
            RaiseDashboardProperties();
            ScanCompleted?.Invoke(this, EventArgs.Empty);
        }

        public void ReleaseOperationTokenSource(CancellationTokenSource cts)
        {
            ReleaseOperationCts(cts);
        }

        public void CancelPendingOperation()
        {
            _operationCoordinator.StateChanged -= OperationCoordinator_StateChanged;
            lock (_operationSync)
            {
                if (_currentOperationCts != null && !_currentOperationCts.IsCancellationRequested)
                {
                    _currentOperationCts.Cancel();
                }
                _currentOperationCts = null;
            }
            _view = null;
        }

        private void ReleaseOperationCts(CancellationTokenSource currentCts)
        {
            lock (_operationSync)
            {
                if (ReferenceEquals(_currentOperationCts, currentCts))
                {
                    _currentOperationCts = null;
                }
            }
            currentCts.Dispose();
        }

        private static string NormalizePath(string path)
        {
            return CleanerPathSafety.NormalizePath(path);
        }

        private class ActionDisposable : IDisposable
        {
            private readonly Action _action;
            public ActionDisposable(Action action) => _action = action;
            public void Dispose() => _action();
        }

        public CleanerScanScope GetLastScope() => _lastScope;
    }
}


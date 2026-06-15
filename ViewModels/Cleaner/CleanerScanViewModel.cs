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

        public CleanerDriveSelectionViewModel DriveSelection { get; }
        public CleanerCleanupViewModel Cleanup { get; }
        public CleanerSettingsViewModel Settings { get; }

        private ICleanerAssistantViewInteraction? _view;
        private CancellationTokenSource? _currentOperationCts;
        private int _suspendRefreshCount;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private int _progressValue;

        [ObservableProperty]
        private string _statusMainText = "就绪";

        [ObservableProperty]
        private string _statusDetailText = "点击下方的快速扫描，检查垃圾和可清理项。";

        [ObservableProperty]
        private string _lastScanText = "最近扫描：无记录";

        private CleanerScanScope _lastScope = CleanerScanScope.Quick;

        public string ScanModeText => _lastScope == CleanerScanScope.Deep ? "深度扫描已完成" : "快速扫描已完成";

        public CleanerAuditSnapshot? AuditSnapshot { get; private set; }

        public ObservableCollection<CleanerScanItem> SafeItems { get; } = new();
        public ObservableCollection<CleanerScanItem> ReviewItems { get; } = new();
        public ObservableCollection<CleanerScanItem> ViewOnlyItems { get; } = new();
        private readonly List<CleanerScanItem> _allItems = new();

        public IReadOnlyList<CleanerScanItem> AllItems => _allItems;

        public int SafeItemCount => SafeItems.Count;
        public long SafeItemSpaceBytes => SafeItems.Sum(i => i.SizeBytes);
        public string SafeSpaceText => CleanerSizeFormatter.Format(SafeItemSpaceBytes);
        public string SafeCountText => $"{SafeItemCount} 项";
        public bool HasSafeItems => SafeItems.Count > 0;

        public int ReviewItemCount => ReviewItems.Count;
        public long ReviewItemSpaceBytes => ReviewItems.Sum(i => i.SizeBytes);
        public string ReviewSpaceText => CleanerSizeFormatter.Format(ReviewItemSpaceBytes);
        public string ReviewCountText => $"{ReviewItemCount} 项";
        public bool HasReviewItems => ReviewItems.Count > 0;

        public int ViewOnlyItemCount => ViewOnlyItems.Count;
        public long ViewOnlyItemSpaceBytes => ViewOnlyItems.Sum(i => i.SizeBytes);
        public string ViewOnlySpaceText => CleanerSizeFormatter.Format(ViewOnlyItemSpaceBytes);
        public string ViewOnlyCountText => $"{ViewOnlyItemCount} 项";
        public bool HasViewOnlyItems => ViewOnlyItems.Count > 0;
        
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

        public int DeselectedDefaultItemCount => SafeItems.Count(i => i.RiskLevel == CleanerRiskLevel.Low && !i.IsSelected) +
                                                 ReviewItems.Count(i => i.RiskLevel == CleanerRiskLevel.Low && !i.IsSelected);

        public int TotalSelectedItemCount => SelectedSafeItemCount + SelectedReviewItemCount;
        public long TotalSelectedSpaceBytes => SelectedSafeItemSpaceBytes + SelectedReviewItemSpaceBytes;
        public string TotalSelectedSpaceText => CleanerSizeFormatter.Format(TotalSelectedSpaceBytes);

        public bool CanRunCleanup => TotalSelectedItemCount > 0 && !IsBusy;
        public bool CanRunAutomaticLowRiskCleanupNow => SafeItems.Count > 0 && !IsBusy;

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
            CleanerSettingsViewModel settings)
        {
            _scanService = scanService;
            _deepScanService = deepScanService;
            _auditService = auditService;
            _stateStore = stateStore;
            DriveSelection = driveSelection;
            Cleanup = cleanup;
            Settings = settings;

            WeakReferenceMessenger.Default.Register<BlueSapphire.Services.StartQuickScanMessage>(this, async (r, m) => await StartQuickScan());
        }

        public async Task InitializeAsync(ICleanerAssistantViewInteraction view)
        {
            _view = view;
            AuditSnapshot = await _auditService.LoadSnapshotAsync();
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
        private void CancelCurrentOperation()
        {
            if (_currentOperationCts != null && !_currentOperationCts.IsCancellationRequested)
            {
                StatusDetailText = "正在取消操作...";
                _currentOperationCts.Cancel();
            }
        }

        private async Task StartScanAsync(CleanerScanScope scope)
        {
            if (IsBusy) return;

            ScanStarted?.Invoke(this, scope);
            _lastScope = scope;
            SetBusyState(true, scope == CleanerScanScope.Quick ? "正在快速扫描..." : "正在深度扫描...", "准备扫描规则和驱动器");

            CancellationTokenSource cts = new();
            _currentOperationCts = cts;

            var progress = new Progress<CleanerScanProgress>(e =>
            {
                ProgressValue = (int)e.ProgressValue;
                if (!string.IsNullOrWhiteSpace(e.Detail))
                {
                    StatusDetailText = e.Detail;
                }
            });

            try
            {
                CleanerScanReport finalReport;
                CleanerScanAddOnResult spaceAnalysisResult = default;
                CleanerScanAddOnResult orphanResult = default;

                CleanerScanOptions options = new CleanerScanOptions
                {
                    AnalysisDriveRoots = DriveSelection.DriveOptions.Where(d => d.IsSelected).Select(d => d.RootPath).ToList(),
                    IncludeLargeObjectAnalysis = scope == CleanerScanScope.Deep,
                    IncludeOrphanResidueAnalysis = scope == CleanerScanScope.Deep
                };

                if (scope == CleanerScanScope.Deep)
                {
                    var deepResult = await _deepScanService.ScanAsync(options, (IProgress<CleanerScanProgress>)progress, cts.Token);
                    finalReport = deepResult.Report;
                    spaceAnalysisResult = deepResult.SpaceAnalysis;
                    orphanResult = deepResult.OrphanResidue;
                }
                else
                {
                    finalReport = await _scanService.ScanAsync(scope, options, (IProgress<CleanerScanProgress>)progress, cts.Token);
                }

                ReplaceScanItems(finalReport.Items);

                await _auditService.RecordScanAsync(finalReport);
                AuditSnapshot = await _auditService.LoadSnapshotAsync();

                LastScanText = finalReport.UsedIncrementalReuse && finalReport.ReusedItemCount > 0
                    ? $"最近扫描：{finalReport.CreatedAt:yyyy-MM-dd HH:mm:ss} · 复用 {finalReport.ReusedItemCount} 项"
                    : $"最近扫描：{finalReport.CreatedAt:yyyy-MM-dd HH:mm:ss}";

                StatusMainText = scope == CleanerScanScope.Quick ? "快速扫描完成" : "深度扫描完成（含抽样分析）";
                StatusDetailText = BuildScanCompletionText(finalReport, spaceAnalysisResult, orphanResult);
                
                ScanCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                StatusMainText = "已取消";
                StatusDetailText = "扫描任务已被取消。";
            }
            catch (Exception ex)
            {
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

        private string BuildScanCompletionText(CleanerScanReport report, CleanerScanAddOnResult spaceAnalysisResult, CleanerScanAddOnResult orphanResult)
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
                    ? "抽样空间占用分析已跳过"
                    : spaceAnalysisResult.AddedCount > 0 ? $"补充 {spaceAnalysisResult.AddedCount} 项空间分析" : "未发现大文件");
            }

            if (orphanResult.Attempted)
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
            SafeItems.Clear();
            ReviewItems.Clear();
            ViewOnlyItems.Clear();

            HashSet<string> exclusions = Cleanup.GetExclusionLookup();

            foreach (CleanerScanItem item in items)
            {
                PrepareScanItem(item, exclusions);

                _allItems.Add(item);
                switch (item.RiskLevel)
                {
                    case CleanerRiskLevel.Low:
                        SafeItems.Add(item);
                        break;
                    case CleanerRiskLevel.Medium:
                        ReviewItems.Add(item);
                        break;
                    case CleanerRiskLevel.High:
                        ViewOnlyItems.Add(item);
                        break;
                }

                item.PropertyChanged += ScanItem_PropertyChanged;
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
                item.IsSelected = item.RiskLevel == CleanerRiskLevel.Low;
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
            OnPropertyChanged(nameof(HasSafeItems));

            OnPropertyChanged(nameof(ReviewItemCount));
            OnPropertyChanged(nameof(ReviewItemSpaceBytes));
            OnPropertyChanged(nameof(ReviewSpaceText));
            OnPropertyChanged(nameof(HasReviewItems));

            OnPropertyChanged(nameof(ViewOnlyItemCount));
            OnPropertyChanged(nameof(ViewOnlyItemSpaceBytes));
            OnPropertyChanged(nameof(ViewOnlySpaceText));
            OnPropertyChanged(nameof(HasViewOnlyItems));

            OnPropertyChanged(nameof(SelectedSafeItemCount));
            OnPropertyChanged(nameof(SelectedSafeItemSpaceBytes));
            OnPropertyChanged(nameof(SelectedSafeSpaceText));

            OnPropertyChanged(nameof(SelectedReviewItemCount));
            OnPropertyChanged(nameof(SelectedReviewItemSpaceBytes));
            OnPropertyChanged(nameof(SelectedReviewSpaceText));

            OnPropertyChanged(nameof(DeselectedDefaultItemCount));
            OnPropertyChanged(nameof(TotalSelectedItemCount));
            OnPropertyChanged(nameof(TotalSelectedSpaceBytes));
            OnPropertyChanged(nameof(TotalSelectedSpaceText));

            OnPropertyChanged(nameof(CanRunCleanup));
            OnPropertyChanged(nameof(CanRunAutomaticLowRiskCleanupNow));

            DashboardChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetBusyState(bool isBusy, string mainText, string detailText)
        {
            IsBusy = isBusy;
            StatusMainText = mainText;
            StatusDetailText = detailText;
            ProgressValue = isBusy ? 0 : 100;
            RaiseDashboardProperties();
        }

        private void ReleaseOperationCts(CancellationTokenSource currentCts)
        {
            if (_currentOperationCts == currentCts)
            {
                _currentOperationCts = null;
            }
            currentCts.Dispose();
        }

        private static string NormalizePath(string path)
        {
            return path.TrimEnd('\\', '/') + "\\";
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


using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.Services;
using BlueSapphire.ViewModels.Cleaner;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.ViewModels
{
    public partial class CleanerAssistantViewModel : ObservableObject
    {
        private readonly CleanerExecutionService _executionService;
        private readonly CleanerAuditService _auditService;
        private readonly CleanerPrivilegeService _privilegeService;
        private readonly CleanerLaunchActionService _launchActionService;
        private readonly CleanerAutomationService _automationService;
        private readonly CleanerRecommendationService _recommendationService;
        private readonly NativeFileService _nativeFileService;
        private readonly AITaskCenterService? _taskCenter;
        private readonly CleanerOperationCoordinator _operationCoordinator;
        private int _ruleRescanPending;
        private int _ruleRescanRunning;
        private readonly object _ruleRescanWaitSync = new();
        private TaskCompletionSource _ruleRescanIdle = CreateCompletedTaskSource();
        private bool _isShuttingDown;

        private ICleanerAssistantViewInteraction? _view;

        [ObservableProperty]
        public partial bool IsCleanupRunning { get; set; }

        [ObservableProperty]
        public partial bool IsCleanupOutcomeVisible { get; set; }

        [ObservableProperty]
        public partial bool CleanupOutcomeHasFailures { get; set; }

        [ObservableProperty]
        public partial int CleanupProgressValue { get; set; }

        [ObservableProperty]
        public partial int CleanupProgressMax { get; set; } = 100;

        [ObservableProperty]
        public partial string CleanupStageText { get; set; } = "准备清理";

        [ObservableProperty]
        public partial string CleanupDetailText { get; set; } = "正在核对清理计划";

        [ObservableProperty]
        public partial string CleanupProgressText { get; set; } = "0 / 0 项";

        [ObservableProperty]
        public partial string CleanupOutcomeTitle { get; set; } = "清理完成";

        [ObservableProperty]
        public partial string CleanupOutcomeDetail { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CleanupReleasedText { get; set; } = "0 B";

        [ObservableProperty]
        public partial string CleanupRecoverableText { get; set; } = "0 B";

        [ObservableProperty]
        public partial string CleanupFailedText { get; set; } = "0 项";

        public bool IsCleanupExperienceVisible => IsCleanupRunning || IsCleanupOutcomeVisible;

        partial void OnIsCleanupRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(IsCleanupExperienceVisible));
        }

        partial void OnIsCleanupOutcomeVisibleChanged(bool value)
        {
            OnPropertyChanged(nameof(IsCleanupExperienceVisible));
        }

        public CleanerScanViewModel Scan { get; }
        public CleanerCleanupViewModel Cleanup { get; }
        public CleanerAutomationViewModel Automation { get; }
        public CleanerRuleManagementViewModel Rule { get; }
        public CleanerDriveSelectionViewModel Drive { get; }
        public CleanerSettingsViewModel Settings { get; }

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
            CleanerSettingsViewModel settings,
            AITaskCenterService? taskCenter = null,
            AISharedContextService? sharedContext = null,
            CleanerOperationCoordinator? operationCoordinator = null)
        {
            _executionService = executionService;
            _auditService = auditService;
            _privilegeService = privilegeService;
            _launchActionService = launchActionService;
            _automationService = automationService;
            _recommendationService = recommendationService;
            _nativeFileService = nativeFileService;
            _taskCenter = taskCenter;
            _operationCoordinator = operationCoordinator ?? new CleanerOperationCoordinator();
            Settings = settings;

            Drive = new CleanerDriveSelectionViewModel(driveService, stateStore);
            Cleanup = new CleanerCleanupViewModel(executionService, stateStore, nativeFileService, auditService, _operationCoordinator);
            Scan = new CleanerScanViewModel(
                scanService,
                deepScanService,
                auditService,
                stateStore,
                Drive,
                Cleanup,
                settings,
                taskCenter,
                sharedContext,
                _operationCoordinator);
            Cleanup.SharedOperationIdleProvider = () => !Scan.IsBusy;
            Rule = new CleanerRuleManagementViewModel(ruleService, telemetryService, profileService, stateStore, nativeFileService, settings, auditService);
            Automation = new CleanerAutomationViewModel(automationService, settings);

            Cleanup.RetryRequested += async (s, batchId) => await RetryFailedCleanupEntriesCoreAsync(batchId, "重试");
            Cleanup.OperationStateChanged += Cleanup_OperationStateChanged;
            Rule.ScanInvalidated += (s, e) => QueueRuleTriggeredRescan();
            Scan.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Scan.IsBusy) && !Scan.IsBusy)
                {
                    StartPendingRuleRescan();
                }
                if (e.PropertyName == nameof(Scan.IsBusy))
                {
                    Cleanup.NotifySharedOperationStateChanged();
                }
            };

            Scan.DashboardChanged += (s, e) => RaiseDashboardProperties();
            Scan.ScanStarted += (s, e) => SelectedScanItem = null;
            Scan.ScanCompleted += (s, e) =>
            {
                if (SelectedScanItem != null && !Scan.AllItems.Contains(SelectedScanItem))
                {
                    SelectedScanItem = null;
                }
                RaiseDashboardProperties();
            };
            Cleanup.ExclusionsChanged += (s, e) =>
            {
                Scan.RefreshExclusionState();
                if (SelectedScanItem?.IsExcluded == true)
                {
                    SelectedScanItem = null;
                }
                RaiseDashboardProperties();
            };
            Cleanup.ExclusionListChanged += (s, e) => QueueRuleTriggeredRescan();

            WeakReferenceMessenger.Default.Register<BlueSapphire.Models.RunAutomaticLowRiskCleanupMessage>(this, async (r, m) =>
            {
                await ExecuteAutomaticLowRiskCleanupAsync("立即自动保洁", showCompletionTip: true);
            });
            WeakReferenceMessenger.Default.Register<BlueSapphire.Models.StartQuickScanMessage>(this, async (r, m) => 
            {
                if (!Scan.IsBusy) await Scan.StartQuickScanCommand.ExecuteAsync(null);
            });
            WeakReferenceMessenger.Default.Register<BlueSapphire.Models.RunCleanupMessage>(this, async (r, m) =>
            {
                if (!Scan.IsBusy) await RunCleanup();
            });
        }

        public async Task InitializeAsync(ICleanerAssistantViewInteraction view)
        {
            _view = view;
            
            await Drive.InitializeAsync();
            await Settings.InitializeQuarantineSettingsAsync();
            await Cleanup.InitializeAsync(view);
            await Automation.InitializeAsync();
            await Rule.InitializeAsync(view);
            await Scan.InitializeAsync(view);
            
            await HandleLaunchActionsAsync();
            await HandleAutomationAsync();
        }

        [RelayCommand]
        private async Task RunCleanup()
        {
            if (_view == null || Scan.IsBusy) return;

            List<CleanerScanItem> selectedItems = Scan.AllItems
                .Where(item => item.IsSelected && item.IsSelectableAndEnabled)
                .ToList();

            if (selectedItems.Count == 0)
            {
                await _view.ShowTipAsync("没有可执行项", "先勾选你希望纳入本次清理的对象。");
                return;
            }

            CleanerCleanupPlanSummary cleanupPlan = CleanerCleanupPlanSummary.FromItems(selectedItems);
            bool confirmed = await _view.ShowCleanupConfirmationAsync(cleanupPlan);

            if (!confirmed) return;

            if (!_operationCoordinator.TryAcquire(CleanerOperationKind.Cleanup, out CleanerOperationLease? operationLease))
            {
                await _view.ShowTipAsync("清理助手正忙", "另一项扫描、清理或恢复任务正在执行，请稍后再试。");
                return;
            }

            bool refreshAfterCleanup = false;
            using (operationLease)
            {
                CancellationTokenSource cts = Scan.CreateOperationTokenSource();
            string idempotencyKey = $"cleaner.ui.execute:{string.Join(
                "|",
                selectedItems.Select(item => item.ObjectId).OrderBy(id => id, StringComparer.Ordinal))}";
                using AITaskLease? task = _taskCenter?.Begin(
                "cleaner.execute",
                "清理任务",
                $"处理 {selectedItems.Count} 项，预计释放 {CleanerSizeFormatter.Format(selectedItems.Sum(item => item.SizeBytes))}",
                idempotencyKey);
                if (task?.IsDuplicate == true)
                {
                    Scan.ReleaseOperationTokenSource(cts);
                    await _view.ShowTipAsync("任务已存在", "相同的清理任务正在执行或刚刚完成，本次不会重复执行。");
                    return;
                }
                using CancellationTokenSource linkedCts = task == null
                    ? CancellationTokenSource.CreateLinkedTokenSource(cts.Token)
                    : CancellationTokenSource.CreateLinkedTokenSource(cts.Token, task.Token);
                BeginCleanupExperience(selectedItems);
                try
                {
                Scan.SetBusyState(true, "正在执行清理", "优先使用隔离区和回收站，避免不可逆误删。");
                Progress<CleanerExecutionProgress> progress = new(value =>
                {
                    UpdateExecutionProgress(value);
                    if (task != null)
                    {
                        double percent = value.ProgressMax > 0
                            ? value.ProgressValue / value.ProgressMax * 100
                            : 0;
                        _taskCenter?.Report(task.TaskId, percent, value.StageTitle, value.Detail);
                    }
                });
                CleanerCleanupBatch batch = await _executionService.ExecuteAsync(selectedItems, Scan.GetLastScope(), progress, linkedCts.Token);
                
                Cleanup.ApplyLatestBatch(batch);
                await _auditService.RecordDeselectionAsync(Scan.AllItems);
                await _auditService.RecordCleanupAsync(batch, Scan.DeselectedDefaultItemCount);

                CompleteCleanupExperience(batch);
                if (task != null)
                {
                    _taskCenter?.Complete(
                        task.TaskId,
                        $"清理完成：{batch.OutcomeText}，失败 {batch.FailedCount} 项。");
                }

                await Cleanup.ReloadHistoryAndExclusionsAsync();
                refreshAfterCleanup = true;
                }
                catch (OperationCanceledException)
                {
                if (task != null)
                {
                    _taskCenter?.MarkCancelled(task.TaskId);
                }
                ShowCleanupInterruption(
                    "清理已取消",
                    "任务已停止；已经完成的项目不会被重复处理，可在“记录”中核对结果。",
                    hasFailures: true);
                await Cleanup.ReloadHistoryAndExclusionsAsync();
                }
                catch (Exception ex)
                {
                if (task != null)
                {
                    _taskCenter?.Fail(task.TaskId, ex.Message);
                }
                ShowCleanupInterruption(
                    "清理未完成",
                    $"执行过程中遇到问题：{ex.Message}",
                    hasFailures: true);
                }
                finally
                {
                    Scan.ReleaseOperationTokenSource(cts);
                    Scan.SetBusyState(false, Scan.StatusMainText, Scan.StatusDetailText);
                }
            }

            if (refreshAfterCleanup)
            {
                await Scan.StartQuickScanCommand.ExecuteAsync(null);
            }
        }

        private void UpdateExecutionProgress(CleanerExecutionProgress p)
        {
            int max = Math.Max(1, (int)Math.Ceiling(p.ProgressMax));
            int value = Math.Clamp((int)Math.Ceiling(p.ProgressValue), 0, max);
            CleanupProgressMax = max;
            CleanupProgressValue = value;
            CleanupProgressText = $"{value} / {max} 项";
            CleanupStageText = string.IsNullOrWhiteSpace(p.StageTitle) ? "正在执行清理" : p.StageTitle;
            Scan.ProgressValue = (int)Math.Round((double)value / max * 100);
            if (!string.IsNullOrWhiteSpace(p.Detail))
            {
                CleanupDetailText = p.Detail;
                Scan.StatusDetailText = p.Detail;
            }
        }

        [RelayCommand]
        private void DismissCleanupOutcome()
        {
            IsCleanupOutcomeVisible = false;
        }

        private void BeginCleanupExperience(IReadOnlyCollection<CleanerScanItem> selectedItems)
        {
            IsCleanupOutcomeVisible = false;
            CleanupOutcomeHasFailures = false;
            CleanupProgressValue = 0;
            CleanupProgressMax = Math.Max(1, selectedItems.Count);
            CleanupProgressText = $"0 / {selectedItems.Count} 项";
            CleanupStageText = "正在建立安全清理任务";
            CleanupDetailText = "可恢复项目会优先进入隔离区，执行期间可以随时取消。";
            IsCleanupRunning = true;
        }

        private void CompleteCleanupExperience(CleanerCleanupBatch batch)
        {
            CleanupProgressValue = CleanupProgressMax;
            CleanupProgressText = $"{CleanupProgressMax} / {CleanupProgressMax} 项";
            CleanupOutcomeHasFailures = batch.FailedCount > 0;
            CleanupOutcomeTitle = batch.FailedCount > 0
                ? $"清理完成，{batch.FailedCount} 项需要处理"
                : "清理完成";
            CleanupOutcomeDetail = batch.FailedCount > 0
                ? $"{batch.OutcomeText}。失败项目已保留，未发生强制删除。"
                : $"{batch.OutcomeText}。正在后台刷新剩余可清理项目。";
            CleanupReleasedText = CleanerSizeFormatter.Format(batch.ReleasedBytes);
            CleanupRecoverableText = CleanerSizeFormatter.Format(batch.RecoverableBytes);
            CleanupFailedText = $"{batch.FailedCount} 项";
            IsCleanupOutcomeVisible = true;
            IsCleanupRunning = false;
        }

        private void ShowCleanupInterruption(string title, string detail, bool hasFailures)
        {
            CleanupOutcomeHasFailures = hasFailures;
            CleanupOutcomeTitle = title;
            CleanupOutcomeDetail = detail;
            CleanupReleasedText = "查看记录";
            CleanupRecoverableText = "以实际记录为准";
            CleanupFailedText = "待核对";
            IsCleanupOutcomeVisible = true;
            IsCleanupRunning = false;
        }

        [RelayCommand]
        private async Task EnterElevatedMode()
        {
            if (_privilegeService.IsElevated)
            {
                if (_view != null) await _view.ShowTipAsync("已在管理员模式", "当前实例已经拥有系统最高权限。");
                return;
            }

            try
            {
                await _privilegeService.RestartElevatedAsync();
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("提权失败", ex.Message);
            }
        }

        [RelayCommand]
        private async Task EnterElevatedModeAndRetryFailures()
        {
            if (_privilegeService.IsElevated)
            {
                if (_view != null) await _view.ShowTipAsync("已在管理员模式", "当前实例已经拥有系统最高权限，无需提权即可重试。");
                return;
            }
            try
            {
                await _privilegeService.RestartElevatedAsync(extraArguments: new[] { "--cleaner-retry-batch=LATEST" });
            }
            catch (Exception ex)
            {
                if (_view != null) await _view.ShowTipAsync("提权失败", ex.Message);
            }
        }

        private async Task RetryFailedCleanupEntriesCoreAsync(string? batchId, string completionTitle)
        {
            if (string.IsNullOrWhiteSpace(batchId) || _view == null || Scan.IsBusy) return;
            if (!_operationCoordinator.TryAcquire(CleanerOperationKind.Retry, out CleanerOperationLease? operationLease))
            {
                await _view.ShowTipAsync("清理助手正忙", "另一项扫描、清理或恢复任务正在执行，请稍后再试。");
                return;
            }

            bool refreshAfterRetry = false;
            using (operationLease)
            {
                CancellationTokenSource cts = Scan.CreateOperationTokenSource();
                try
                {
                    Scan.SetBusyState(true, "正在重试失败项", "部分项可能需要更高权限或解开占用");
                    Progress<CleanerExecutionProgress> progress = new(UpdateExecutionProgress);
                    CleanerCleanupBatch? batch = await _executionService.RetryFailedEntriesAsync(batchId, progress, cts.Token);
                
                if (batch != null)
                {
                    Cleanup.ApplyLatestBatch(batch);
                    await Cleanup.ReloadHistoryAndExclusionsAsync();

                    string resultMessage = batch.FailedCount > 0
                        ? $"重试完成，{batch.OutcomeText}，但仍有 {batch.FailedCount} 项失败。"
                        : $"重试完成，{batch.OutcomeText}，全部成功。";

                    if (_view != null) await _view.ShowTipAsync(completionTitle, resultMessage);
                    refreshAfterRetry = true;
                }
                }
                catch (OperationCanceledException)
                {
                    if (_view != null) await _view.ShowTipAsync("已取消", "重试任务已被取消。");
                }
                catch (Exception ex)
                {
                    if (_view != null) await _view.ShowTipAsync("重试失败", ex.Message);
                }
                finally
                {
                    Scan.ReleaseOperationTokenSource(cts);
                    Scan.SetBusyState(false, Scan.StatusMainText, Scan.StatusDetailText);
                }
            }

            if (refreshAfterRetry)
            {
                await Scan.StartQuickScanCommand.ExecuteAsync(null);
            }
        }

        private async Task HandleLaunchActionsAsync()
        {
            string? retryBatchId = _launchActionService.ConsumeRetryBatchId();
            if (!string.IsNullOrWhiteSpace(retryBatchId))
            {
                await RetryFailedCleanupEntriesCoreAsync(retryBatchId, "管理员模式重试");
            }
        }

        private async Task HandleAutomationAsync()
        {
            CleanerAutomationStatus status = await _automationService.LoadStatusAsync();

            if (status.AutoLowRiskCleanupEnabled && status.IsAutoCleanupDue)
            {
                await ExecuteAutomaticLowRiskCleanupAsync("定期自动保洁", showCompletionTip: true);
                return;
            }

            if (status.ReminderEnabled && status.IsReminderDue)
            {
                if (_view != null)
                {
                    bool confirmed = await _view.ShowScanReminderConfirmationAsync();
                    if (confirmed)
                    {
                        await Scan.StartQuickScanCommand.ExecuteAsync(null);
                    }
                }
                status = await _automationService.MarkReminderHandledAsync();
                Automation.ApplyAutomationStatus(status);
            }
        }

        public async Task ExecuteAutomaticLowRiskCleanupAsync(string title, bool showCompletionTip)
        {
            if (Scan.IsBusy) return;

            if (!_operationCoordinator.TryAcquire(CleanerOperationKind.AutomaticCleanup, out CleanerOperationLease? operationLease))
            {
                if (showCompletionTip && _view != null)
                {
                    await _view.ShowTipAsync(title, "清理助手正在执行另一项任务，本轮自动保洁已跳过。");
                }
                return;
            }

            bool refreshAfterCleanup = false;
            using (operationLease)
            {
                bool scanCompleted = await Scan.RunScanWithinOperationAsync(CleanerScanScope.Quick, operationLease!);
                if (!scanCompleted)
                {
                    if (showCompletionTip && _view != null)
                    {
                        await _view.ShowTipAsync(title, "扫描未完成，未执行自动清理。");
                    }
                    return;
                }

                CancellationTokenSource cts = Scan.CreateOperationTokenSource();
                try
                {
                List<CleanerScanItem> safeItems = Scan.SafeItems
                    .Where(item =>
                        item.IsSelected &&
                        item.IsSelectableAndEnabled &&
                        item.RiskLevel == CleanerRiskLevel.Low &&
                        item.ExecutionMode is CleanerExecutionMode.Quarantine or CleanerExecutionMode.Recycle)
                    .ToList();
                if (safeItems.Count == 0)
                {
                    CleanerAutomationStatus emptyStatus = await _automationService.MarkAutoCleanupHandledAsync();
                    Automation.ApplyAutomationStatus(emptyStatus);
                    if (showCompletionTip && _view != null) await _view.ShowTipAsync(title, "没有发现需要清理的安全垃圾。");
                    return;
                }

                BeginCleanupExperience(safeItems);
                Scan.SetBusyState(true, $"{title}清理中...", "正在清除...");
                Progress<CleanerExecutionProgress> progress = new(UpdateExecutionProgress);
                CleanerCleanupBatch batch = await _executionService.ExecuteAsync(safeItems, CleanerScanScope.Quick, progress, cts.Token);
                
                await _auditService.RecordCleanupAsync(batch, manualDeselections: 0);
                CleanerAutomationStatus status = await _automationService.MarkAutoCleanupHandledAsync();
                Automation.ApplyAutomationStatus(status);
                Cleanup.ApplyLatestBatch(batch);
                await Cleanup.ReloadHistoryAndExclusionsAsync();
                CompleteCleanupExperience(batch);
                refreshAfterCleanup = true;

                if (showCompletionTip && _view != null)
                {
                    await _view.ShowTipAsync(title, $"自动保洁完毕：{batch.OutcomeText}。");
                }
                }
                catch (OperationCanceledException)
                {
                if (IsCleanupRunning)
                {
                    ShowCleanupInterruption("自动保洁已取消", "任务已停止，已完成的项目可在记录中核对。", hasFailures: true);
                }
                if (showCompletionTip && _view != null) await _view.ShowTipAsync("已取消", "自动保洁操作已被取消。");
                }
                catch (Exception ex)
                {
                if (IsCleanupRunning)
                {
                    ShowCleanupInterruption("自动保洁未完成", ex.Message, hasFailures: true);
                }
                if (showCompletionTip && _view != null) await _view.ShowTipAsync("自动保洁失败", ex.Message);
                }
                finally
                {
                    Scan.ReleaseOperationTokenSource(cts);
                    Scan.SetBusyState(false, "自动保洁完成", "");
                }
            }

            if (refreshAfterCleanup)
            {
                await Scan.StartQuickScanCommand.ExecuteAsync(null);
            }
        }

        private void Cleanup_OperationStateChanged(bool isRunning, string operationName)
        {
            Scan.SetBusyState(
                isRunning,
                isRunning ? $"正在{operationName}" : $"{operationName}完成",
                isRunning ? "正在安全处理隔离区记录，请勿同时启动扫描或清理。" : "记录和隔离区状态已刷新。");
        }

        private void QueueRuleTriggeredRescan()
        {
            if (_isShuttingDown) return;
            lock (_ruleRescanWaitSync)
            {
                Interlocked.Exchange(ref _ruleRescanPending, 1);
                if (_ruleRescanIdle.Task.IsCompleted)
                {
                    _ruleRescanIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            StartPendingRuleRescan();
        }

        private void StartPendingRuleRescan()
        {
            if (_isShuttingDown || Scan.IsBusy || Volatile.Read(ref _ruleRescanPending) == 0)
            {
                return;
            }

            _ = TryRunPendingRuleRescanAsync();
        }

        private async Task TryRunPendingRuleRescanAsync()
        {
            if (_isShuttingDown || Scan.IsBusy ||
                Interlocked.CompareExchange(ref _ruleRescanRunning, 1, 0) != 0)
            {
                return;
            }

            try
            {
                while (!_isShuttingDown && Interlocked.Exchange(ref _ruleRescanPending, 0) == 1)
                {
                    if (Scan.IsBusy)
                    {
                        Interlocked.Exchange(ref _ruleRescanPending, 1);
                        return;
                    }

                    await Scan.RunScanAsync(CleanerScanScope.Quick);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _ruleRescanRunning, 0);
                bool shouldRetry = !_isShuttingDown &&
                    !Scan.IsBusy &&
                    Volatile.Read(ref _ruleRescanPending) == 1;
                if (shouldRetry)
                {
                    StartPendingRuleRescan();
                }
                else if (_isShuttingDown || Volatile.Read(ref _ruleRescanPending) == 0)
                {
                    lock (_ruleRescanWaitSync)
                    {
                        _ruleRescanIdle.TrySetResult();
                    }
                }
            }
        }

        public Task WaitForPendingBackgroundWorkAsync()
        {
            lock (_ruleRescanWaitSync)
            {
                return _ruleRescanIdle.Task;
            }
        }

        private static TaskCompletionSource CreateCompletedTaskSource()
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult();
            return source;
        }

        public void Shutdown()
        {
            _isShuttingDown = true;
            Interlocked.Exchange(ref _ruleRescanPending, 0);
            Scan.CancelPendingOperation();
            Cleanup.CancelPendingOperations();
            Rule.Shutdown();
            Settings.Shutdown();
            WeakReferenceMessenger.Default.UnregisterAll(this);
            _view = null;
            lock (_ruleRescanWaitSync)
            {
                if (Volatile.Read(ref _ruleRescanRunning) == 0)
                {
                    _ruleRescanIdle.TrySetResult();
                }
            }
        }
    }
}




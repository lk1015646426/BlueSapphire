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

        private ICleanerAssistantViewInteraction? _view;

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
            AISharedContextService? sharedContext = null)
        {
            _executionService = executionService;
            _auditService = auditService;
            _privilegeService = privilegeService;
            _launchActionService = launchActionService;
            _automationService = automationService;
            _recommendationService = recommendationService;
            _nativeFileService = nativeFileService;
            _taskCenter = taskCenter;
            Settings = settings;

            Drive = new CleanerDriveSelectionViewModel(driveService, stateStore);
            Cleanup = new CleanerCleanupViewModel(executionService, stateStore, nativeFileService);
            Scan = new CleanerScanViewModel(
                scanService,
                deepScanService,
                auditService,
                stateStore,
                Drive,
                Cleanup,
                settings,
                taskCenter,
                sharedContext);
            Rule = new CleanerRuleManagementViewModel(ruleService, telemetryService, profileService, stateStore, nativeFileService, settings, auditService);
            Automation = new CleanerAutomationViewModel(automationService, settings);

            Cleanup.RetryRequested += async (s, batchId) => await RetryFailedCleanupEntriesCoreAsync(batchId, "重试");
            Rule.ScanInvalidated += async (s, e) => { if (!Scan.IsBusy) { await Scan.StartQuickScanCommand.ExecuteAsync(null); } };

            Scan.DashboardChanged += (s, e) => RaiseDashboardProperties();
            Scan.ScanCompleted += (s, e) => RaiseDashboardProperties();
            Cleanup.ExclusionsChanged += (s, e) => RaiseDashboardProperties();

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

            bool confirmed = await _view.ShowCleanupConfirmationAsync(
                selectedItems.Count,
                CleanerSizeFormatter.Format(selectedItems.Sum(item => item.SizeBytes)),
                selectedItems.Any(item => item.RiskLevel == CleanerRiskLevel.Medium));

            if (!confirmed) return;

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
                
                string failureSummary = batch.FailedCount > 0 ? $"\n失败 {batch.FailedCount} 项。" : string.Empty;
                if (_view != null)
                {
                    await _view.ShowTipAsync("清理完成", $"本次释放 {CleanerSizeFormatter.Format(batch.ReleasedBytes)}，共处理 {batch.Entries.Count} 个对象。{failureSummary}");
                }
                if (task != null)
                {
                    _taskCenter?.Complete(
                        task.TaskId,
                        $"清理完成：释放 {CleanerSizeFormatter.Format(batch.ReleasedBytes)}，失败 {batch.FailedCount} 项。");
                }

                await Scan.StartQuickScanCommand.ExecuteAsync(null);
                await Cleanup.ReloadHistoryAndExclusionsAsync();
            }
            catch (OperationCanceledException)
            {
                if (task != null)
                {
                    _taskCenter?.MarkCancelled(task.TaskId);
                }
                if (_view != null) await _view.ShowTipAsync("已取消", "清理任务已被取消。");
            }
            catch (Exception ex)
            {
                if (task != null)
                {
                    _taskCenter?.Fail(task.TaskId, ex.Message);
                }
                if (_view != null) await _view.ShowTipAsync("清理失败", ex.Message);
            }
            finally
            {
                Scan.ReleaseOperationTokenSource(cts);
                Scan.SetBusyState(false, Scan.StatusMainText, Scan.StatusDetailText);
            }
        }

        private void UpdateExecutionProgress(CleanerExecutionProgress p)
        {
            Scan.ProgressValue = (int)p.ProgressValue;
            if (!string.IsNullOrWhiteSpace(p.Detail))
            {
                Scan.StatusDetailText = p.Detail;
            }
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
            if (string.IsNullOrWhiteSpace(batchId) || _view == null) return;
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
                        ? $"重试完成，释放 {CleanerSizeFormatter.Format(batch.ReleasedBytes)}，但仍有 {batch.FailedCount} 项失败。"
                        : $"重试圆满完成，释放 {CleanerSizeFormatter.Format(batch.ReleasedBytes)}，全部成功！";

                    if (_view != null) await _view.ShowTipAsync(completionTitle, resultMessage);
                    await Scan.StartQuickScanCommand.ExecuteAsync(null);
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
                    bool confirmed = await _view.ShowCleanupConfirmationAsync(0, "0 B", false);
                    if (confirmed)
                    {
                        await Scan.StartQuickScanCommand.ExecuteAsync(null);
                    }
                }
                status = await _automationService.MarkReminderHandledAsync();
                Automation.ApplyAutomationStatus(status);
            }
        }

        private async Task ExecuteAutomaticLowRiskCleanupAsync(string title, bool showCompletionTip)
        {
            if (Scan.IsBusy) return;

            bool scanCompleted = await Scan.RunScanAsync(CleanerScanScope.Quick);
            if (!scanCompleted)
            {
                if (showCompletionTip && _view != null)
                {
                    if (_view != null) await _view.ShowTipAsync(title, "扫描未完成，未执行自动清理。");
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
                        item.ExecutionMode != CleanerExecutionMode.None)
                    .ToList();
                if (safeItems.Count == 0)
                {
                    if (showCompletionTip && _view != null) await _view.ShowTipAsync(title, "没有发现需要清理的安全垃圾。");
                    return;
                }

                Scan.SetBusyState(true, $"{title}清理中...", "正在清除...");
                Progress<CleanerExecutionProgress> progress = new(UpdateExecutionProgress);
                CleanerCleanupBatch batch = await _executionService.ExecuteAsync(safeItems, CleanerScanScope.Quick, progress, cts.Token);
                
                await _auditService.RecordCleanupAsync(batch, manualDeselections: 0);
                CleanerAutomationStatus status = await _automationService.MarkAutoCleanupHandledAsync();
                Automation.ApplyAutomationStatus(status);
                Cleanup.ApplyLatestBatch(batch);
                await Cleanup.ReloadHistoryAndExclusionsAsync();

                if (showCompletionTip && _view != null)
                {
                    await _view.ShowTipAsync(title, $"自动保洁完毕，释放 {CleanerSizeFormatter.Format(batch.ReleasedBytes)}。");
                }
            }
            catch (OperationCanceledException)
            {
                if (showCompletionTip && _view != null) await _view.ShowTipAsync("已取消", "自动保洁操作已被取消。");
            }
            catch (Exception ex)
            {
                if (showCompletionTip && _view != null) await _view.ShowTipAsync("自动保洁失败", ex.Message);
            }
            finally
            {
                Scan.ReleaseOperationTokenSource(cts);
                Scan.SetBusyState(false, "自动保洁完成", "");
            }
        }

        public void Shutdown()
        {
            Scan.CancelPendingOperation();
            Cleanup.CancelPendingOperations();
            Rule.Shutdown();
            Settings.Shutdown();
            WeakReferenceMessenger.Default.UnregisterAll(this);
            _view = null;
        }
    }
}




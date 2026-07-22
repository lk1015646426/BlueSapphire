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
    public partial class CleanerCleanupViewModel : ObservableObject
    {
        private readonly CleanerExecutionService _executionService;
        private readonly CleanerStateStore _stateStore;
        private readonly NativeFileService _nativeFileService;
        private readonly CleanerAuditService _auditService;
        private readonly CleanerOperationCoordinator _operationCoordinator;
        private ICleanerAssistantViewInteraction? _view;
        private readonly object _restoreSync = new();
        private CancellationTokenSource? _restoreCts;
        private readonly CancellationTokenSource _lifetimeCts = new();
        public Func<bool>? SharedOperationIdleProvider { get; set; }

        [ObservableProperty]
        public partial bool IsOperationRunning { get; set; }

        public bool IsOperationIdle => !IsOperationRunning;
        public bool CanStartHistoryOperation => IsOperationIdle &&
            !_operationCoordinator.IsBusy &&
            (SharedOperationIdleProvider?.Invoke() ?? true);

        private CleanerCleanupBatch? _latestBatch;
        private int _quarantineItemCount;
        private long _quarantineBytes;

        public ObservableCollection<CleanerExclusionEntry> Exclusions { get; } = new();
        public ObservableCollection<CleanerCleanupEntry> LatestCleanupEntries { get; } = new();
        public ObservableCollection<CleanerCleanupBatch> CleanupHistoryBatches { get; } = new();

        [ObservableProperty]
        public partial CleanerCleanupBatch? SelectedCleanupBatch { get; set; }

        public bool HasExclusions => Exclusions.Count > 0;
        public bool HasLatestCleanupEntries => LatestCleanupEntries.Count > 0;
        public bool HasCleanupHistory => CleanupHistoryBatches.Count > 0;
        public bool HasLatestCleanupFailures => _latestBatch?.FailedCount > 0;
        public bool HasRestorableBatch => _latestBatch?.Entries.Any(entry => entry.CanRestore && !entry.Restored) == true;
        public bool HasRetryableFailures => _latestBatch?.Entries.Any(entry => entry.CanRetryEntry) == true;
        public bool HasQuarantineEntries => _quarantineItemCount > 0;
        public bool CanRestoreLatest => HasRestorableBatch && CanStartHistoryOperation;
        public bool CanPurgeQuarantine => HasQuarantineEntries && CanStartHistoryOperation;
        public string QuarantineSummaryText => HasQuarantineEntries
            ? $"隔离区 {_quarantineItemCount} 项 · {CleanerSizeFormatter.Format(_quarantineBytes)} 待释放"
            : "隔离区为空";

        public string ExclusionSummaryText => HasExclusions ? $"{Exclusions.Count} 条排除规则" : "暂无排除项";
        public string LatestCleanupSummaryText => _latestBatch?.SummaryText ?? "暂无最近一次清理记录";
        public string LatestCleanupHintText
        {
            get
            {
                if (_latestBatch == null) return "最近一次批次没有可恢复内容。";

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

        public string FailureRecoveryHeadlineText
        {
            get
            {
                var groups = GetLatestFailedEntries()
                    .GroupBy(GetEffectiveFailureReason)
                    .OrderByDescending(group => group.Count())
                    .ToList();

                if (groups.Count == 0) return "最近一次没有需要处理的失败项。";

                var topReason = groups[0].Key;
                return $"最近有 {groups[0].Count()} 项因为{CleanerPresentation.ToFailureReasonText(topReason)}而失败";
            }
        }

        public string FailureRecoveryDetailText
        {
            get
            {
                var entries = GetLatestFailedEntries();
                if (entries.Count == 0) return "大部分失败项（如被占用的文件）会在下次扫描中消失或变为可清理状态。如果有因权限不足导致的失败，可以通过这里的操作尝试重试。";

                int accDeniedCount = entries.Count(e => GetEffectiveFailureReason(e) == CleanerFailureReason.AccessDenied || GetEffectiveFailureReason(e) == CleanerFailureReason.ElevationRequired);
                int inUseCount = entries.Count(e => GetEffectiveFailureReason(e) == CleanerFailureReason.InUse);

                string detail = "";
                if (inUseCount > 0) detail += $"有 {inUseCount} 项文件正被其他程序占用，请关闭相关软件后重试。";
                if (accDeniedCount > 0) detail += $"有 {accDeniedCount} 项需要更高权限才能操作，你可以进入管理员模式进行清理。";

                return string.IsNullOrWhiteSpace(detail) ? "请检查系统状态后重试清理。" : detail;
            }
        }

        public event EventHandler<string>? RetryRequested;
        public event EventHandler? ExclusionsChanged;
        public event EventHandler? ExclusionListChanged;
        public event Action<bool, string>? OperationStateChanged;

        partial void OnIsOperationRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(IsOperationIdle));
            OnPropertyChanged(nameof(CanStartHistoryOperation));
            OnPropertyChanged(nameof(CanRestoreLatest));
            OnPropertyChanged(nameof(CanPurgeQuarantine));
        }

        partial void OnSelectedCleanupBatchChanged(CleanerCleanupBatch? value)
        {
            _latestBatch = value;
            LatestCleanupEntries.Clear();
            if (value != null)
            {
                foreach (CleanerCleanupEntry entry in value.Entries
                    .OrderByDescending(entry => entry.CanRestore)
                    .ThenByDescending(entry => entry.FailureReason != CleanerFailureReason.None))
                {
                    LatestCleanupEntries.Add(entry);
                }
            }

            NotifyPropertiesChanged();
        }

        public void NotifySharedOperationStateChanged()
        {
            OnPropertyChanged(nameof(CanStartHistoryOperation));
            OnPropertyChanged(nameof(CanRestoreLatest));
            OnPropertyChanged(nameof(CanPurgeQuarantine));
        }

        public CleanerCleanupViewModel(
            CleanerExecutionService executionService,
            CleanerStateStore stateStore,
            NativeFileService nativeFileService,
            CleanerAuditService auditService,
            CleanerOperationCoordinator operationCoordinator)
        {
            _executionService = executionService;
            _stateStore = stateStore;
            _nativeFileService = nativeFileService;
            _auditService = auditService;
            _operationCoordinator = operationCoordinator;
        }

        public async Task InitializeAsync(ICleanerAssistantViewInteraction view)
        {
            _view = view;
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            if (preferences.AutoPurgeQuarantineEnabled)
            {
                if (_operationCoordinator.TryAcquire(CleanerOperationKind.Purge, out CleanerOperationLease? operationLease))
                {
                    using (operationLease)
                    {
                        CleanerQuarantinePurgeSummary purge = await _executionService.PurgeExpiredQuarantineAsync(
                            preferences.QuarantineRetentionDays,
                            _lifetimeCts.Token);
                        if (purge.PurgedCount > 0)
                        {
                            await _auditService.RecordQuarantinePurgeAsync(purge);
                        }
                    }
                }
            }
            await ReloadHistoryAndExclusionsAsync();
        }

        public async Task ReloadHistoryAndExclusionsAsync()
        {
            IReadOnlyList<CleanerExclusionEntry> exclusions = await _stateStore.LoadExclusionsAsync();
            IReadOnlyList<CleanerCleanupBatch> history = await _stateStore.LoadHistoryAsync();

            string? selectedBatchId = SelectedCleanupBatch?.BatchId;
            CleanupHistoryBatches.Clear();
            foreach (CleanerCleanupBatch batch in history)
            {
                CleanupHistoryBatches.Add(batch);
            }
            SelectedCleanupBatch = history.FirstOrDefault(batch =>
                string.Equals(batch.BatchId, selectedBatchId, StringComparison.OrdinalIgnoreCase))
                ?? history.FirstOrDefault();
            List<CleanerCleanupEntry> quarantineEntries = history
                .SelectMany(batch => batch.Entries)
                .Where(entry => entry.CanRestore && !entry.Restored && !string.IsNullOrWhiteSpace(entry.BackupPath))
                .ToList();
            _quarantineItemCount = quarantineEntries.Count;
            _quarantineBytes = quarantineEntries.Sum(entry => entry.SizeBytes);

            Exclusions.Clear();
            foreach (CleanerExclusionEntry exclusion in exclusions.OrderByDescending(entry => entry.CreatedAt))
            {
                Exclusions.Add(exclusion);
            }

            NotifyPropertiesChanged();
            ExclusionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public HashSet<string> GetExclusionLookup()
        {
            return Exclusions
                .Select(entry => NormalizePath(entry.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public void ApplyLatestBatch(CleanerCleanupBatch batch)
        {
            CleanerCleanupBatch? existing = CleanupHistoryBatches.FirstOrDefault(candidate =>
                string.Equals(candidate.BatchId, batch.BatchId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                CleanupHistoryBatches.Remove(existing);
            }
            CleanupHistoryBatches.Insert(0, batch);
            SelectedCleanupBatch = batch;
        }

        public List<CleanerCleanupEntry> GetLatestFailedEntries()
        {
            return _latestBatch?.Entries
                .Where(entry => entry.FailureReason != CleanerFailureReason.None)
                .ToList() ?? new List<CleanerCleanupEntry>();
        }

        [RelayCommand]
        private async Task AddToExclusions(CleanerScanItem? item)
        {
            if (item == null) return;
            try
            {
                List<CleanerExclusionEntry> current = (await _stateStore.LoadExclusionsAsync()).ToList();

                if (!Exclusions.Any(x => string.Equals(x.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    CleanerExclusionEntry entry = new()
                    {
                        Path = item.Path,
                        CreatedAt = DateTimeOffset.Now
                    };

                    current.Add(entry);
                    await _stateStore.SaveExclusionsAsync(current);
                    await ReloadHistoryAndExclusionsAsync();
                    ExclusionListChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                WeakReferenceMessenger.Default.Send(new ShowTipMessage("添加排除项失败", ex.Message));
            }
        }

        [RelayCommand]
        private async Task RemoveExclusion(CleanerExclusionEntry? entry)
        {
            if (entry == null) return;

            try
            {
                List<CleanerExclusionEntry> current = (await _stateStore.LoadExclusionsAsync()).ToList();
                var toRemove = current.FirstOrDefault(x => string.Equals(x.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
                if (toRemove != null)
                {
                    current.Remove(toRemove);
                    await _stateStore.SaveExclusionsAsync(current);
                    await ReloadHistoryAndExclusionsAsync();
                    ExclusionListChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                WeakReferenceMessenger.Default.Send(new ShowTipMessage("移除排除项失败", ex.Message));
            }
        }

        [RelayCommand]
        private async Task RestoreLatestCleanup()
        {
            if (_latestBatch == null || _view == null || !CanStartHistoryOperation) return;

            try
            {
                if (!HasRestorableBatch)
                {
                    WeakReferenceMessenger.Default.Send(new ShowTipMessage("没有可恢复项", "最近一次清理中没有生成可恢复的隔离备份。"));
                    return;
                }

                bool confirmed = await _view.ShowRestoreConfirmationAsync(LatestCleanupSummaryText);
                if (!confirmed) return;

                if (!_operationCoordinator.TryAcquire(CleanerOperationKind.Restore, out CleanerOperationLease? operationLease))
                {
                    WeakReferenceMessenger.Default.Send(new ShowTipMessage("清理助手正忙", "另一项扫描、清理或恢复任务正在执行，请稍后再试。"));
                    return;
                }

                const string operationName = "恢复最近一次清理";
                using (operationLease)
                {
                    CancellationTokenSource cts = BeginRestoreOperation(operationName);
                    try
                    {
                        CleanerRestoreSummary summary = await _executionService.RestoreBatchAsync(
                            _latestBatch.BatchId,
                            progress: null,
                            cts.Token);
                        await ReloadHistoryAndExclusionsAsync();
                        await _auditService.RecordRestoreAsync(summary);
                        WeakReferenceMessenger.Default.Send(new ShowTipMessage("恢复完毕", summary.Message));
                    }
                    finally
                    {
                        EndRestoreOperation(cts, operationName);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (_view != null)
                {
                    WeakReferenceMessenger.Default.Send(new ShowTipMessage("已取消", "恢复操作已取消。"));
                }
            }
            catch (Exception ex)
            {
                if (_view != null)
                {
                    WeakReferenceMessenger.Default.Send(new ShowTipMessage("恢复失败", ex.Message));
                }
            }
        }

        [RelayCommand]
        private async Task RestoreCleanupEntry(CleanerCleanupEntry? entry)
        {
            if (entry == null || !entry.CanRestore || _view == null || !CanStartHistoryOperation) return;

            try
            {
                bool confirmed = await _view.ShowRestoreConfirmationAsync($"{entry.ItemName} · {entry.SizeText}");
                if (!confirmed) return;

                if (_latestBatch == null) return;
                if (!_operationCoordinator.TryAcquire(CleanerOperationKind.Restore, out CleanerOperationLease? operationLease))
                {
                    WeakReferenceMessenger.Default.Send(new ShowTipMessage("清理助手正忙", "另一项扫描、清理或恢复任务正在执行，请稍后再试。"));
                    return;
                }

                const string operationName = "恢复清理项";
                using (operationLease)
                {
                    CancellationTokenSource cts = BeginRestoreOperation(operationName);
                    try
                    {
                        CleanerRestoreSummary summary = await _executionService.RestoreEntryAsync(
                            _latestBatch.BatchId,
                            entry.EntryId,
                            cts.Token);
                        await ReloadHistoryAndExclusionsAsync();
                        await _auditService.RecordRestoreAsync(summary);
                        WeakReferenceMessenger.Default.Send(new ShowTipMessage("恢复完毕", summary.Message));
                    }
                    finally
                    {
                        EndRestoreOperation(cts, operationName);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (_view != null)
                {
                    WeakReferenceMessenger.Default.Send(new ShowTipMessage("已取消", "恢复操作已取消。"));
                }
            }
            catch (Exception ex)
            {
                if (_view != null)
                {
                    WeakReferenceMessenger.Default.Send(new ShowTipMessage("恢复失败", ex.Message));
                }
            }
        }

        [RelayCommand]
        private void RetryFailedCleanupEntries()
        {
            if (_latestBatch == null || !CanStartHistoryOperation) return;
            RetryRequested?.Invoke(this, _latestBatch.BatchId);
        }

        [RelayCommand]
        private async Task OpenQuarantine()
        {
            await _nativeFileService.OpenFolderAsync(_stateStore.QuarantineRootPath);
        }

        [RelayCommand]
        private async Task PurgeQuarantine()
        {
            if (_view == null || !HasQuarantineEntries || !CanStartHistoryOperation)
            {
                return;
            }

            bool confirmed = await _view.ShowPurgeQuarantineConfirmationAsync(_quarantineItemCount, _quarantineBytes);
            if (!confirmed)
            {
                return;
            }

            if (!_operationCoordinator.TryAcquire(CleanerOperationKind.Purge, out CleanerOperationLease? operationLease))
            {
                WeakReferenceMessenger.Default.Send(new ShowTipMessage("清理助手正忙", "另一项扫描、清理或恢复任务正在执行，请稍后再试。"));
                return;
            }

            const string operationName = "清空隔离区";
            using (operationLease)
            {
                CancellationTokenSource cts = BeginRestoreOperation(operationName);
                try
                {
                    CleanerQuarantinePurgeSummary summary = await _executionService.PurgeQuarantineAsync(cts.Token);
                    await _auditService.RecordQuarantinePurgeAsync(summary);
                    await ReloadHistoryAndExclusionsAsync();
                    WeakReferenceMessenger.Default.Send(new ShowTipMessage("隔离区已处理", summary.Message));
                }
                catch (OperationCanceledException)
                {
                    WeakReferenceMessenger.Default.Send(new ShowTipMessage("已取消", "清空隔离区操作已取消。"));
                }
                catch (Exception ex)
                {
                    WeakReferenceMessenger.Default.Send(new ShowTipMessage("清空隔离区失败", ex.Message));
                }
                finally
                {
                    EndRestoreOperation(cts, operationName);
                }
            }
        }

        [RelayCommand]
        private async Task OpenCleanupEntryOriginalPath(CleanerCleanupEntry? entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.OriginalPath)) return;
            await _nativeFileService.RevealInExplorerAsync(entry.OriginalPath);
        }

        [RelayCommand]
        private async Task OpenCleanupEntryBackupPath(CleanerCleanupEntry? entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.BackupPath)) return;
            await _nativeFileService.RevealInExplorerAsync(entry.BackupPath);
        }

        public static CleanerFailureReason GetEffectiveFailureReason(CleanerCleanupEntry entry)
        {
            if (entry.FailureReason == CleanerFailureReason.None)
            {
                return CleanerFailureReason.None;
            }

            if (entry.FailureReason == CleanerFailureReason.ElevationRequired)
            {
                return CleanerFailureReason.AccessDenied;
            }

            return entry.FailureReason;
        }

        private static string NormalizePath(string path)
        {
            return CleanerPathSafety.NormalizePath(path);
        }

        private void NotifyPropertiesChanged()
        {
            OnPropertyChanged(nameof(HasExclusions));
            OnPropertyChanged(nameof(HasLatestCleanupEntries));
            OnPropertyChanged(nameof(HasCleanupHistory));
            OnPropertyChanged(nameof(HasLatestCleanupFailures));
            OnPropertyChanged(nameof(HasRestorableBatch));
            OnPropertyChanged(nameof(HasRetryableFailures));
            OnPropertyChanged(nameof(HasQuarantineEntries));
            OnPropertyChanged(nameof(CanRestoreLatest));
            OnPropertyChanged(nameof(CanPurgeQuarantine));
            OnPropertyChanged(nameof(CanStartHistoryOperation));
            OnPropertyChanged(nameof(QuarantineSummaryText));
            OnPropertyChanged(nameof(ExclusionSummaryText));
            OnPropertyChanged(nameof(LatestCleanupSummaryText));
            OnPropertyChanged(nameof(LatestCleanupHintText));
            OnPropertyChanged(nameof(FailureRecoveryHeadlineText));
            OnPropertyChanged(nameof(FailureRecoveryDetailText));
        }

        public void CancelPendingOperations()
        {
            _lifetimeCts.Cancel();
            lock (_restoreSync)
            {
                _restoreCts?.Cancel();
                _restoreCts = null;
            }
            IsOperationRunning = false;
            _view = null;
        }

        private CancellationTokenSource BeginRestoreOperation(string operationName)
        {
            var next = new CancellationTokenSource();
            lock (_restoreSync)
            {
                _restoreCts?.Cancel();
                _restoreCts = next;
            }
            IsOperationRunning = true;
            OperationStateChanged?.Invoke(true, operationName);
            return next;
        }

        private void EndRestoreOperation(CancellationTokenSource cts, string operationName)
        {
            bool completedCurrentOperation = false;
            lock (_restoreSync)
            {
                if (ReferenceEquals(_restoreCts, cts))
                {
                    _restoreCts = null;
                    completedCurrentOperation = true;
                }
            }
            cts.Dispose();
            if (completedCurrentOperation)
            {
                IsOperationRunning = false;
                OperationStateChanged?.Invoke(false, operationName);
            }
        }
    }
}

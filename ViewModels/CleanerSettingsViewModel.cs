using BlueSapphire.Models;
using BlueSapphire.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.ViewModels
{
    public record ReminderIntervalOption(int Days, string Name);

    public partial class CleanerSettingsViewModel : ObservableObject
    {
        private readonly CleanerAutomationService _automationService;
        private readonly CleanerTelemetryService _telemetryService;
        private readonly CleanerStateStore? _stateStore;
        private bool _isUpdatingFromStatus;
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private readonly object _pendingSaveSync = new();
        private CancellationTokenSource? _automationSaveCts;
        private CancellationTokenSource? _telemetrySaveCts;
        private CancellationTokenSource? _quarantineSaveCts;

        public event Action<CleanerAutomationStatus>? AutomationStatusSaved;
        public event Action<CleanerTelemetryStatus>? TelemetryStatusSaved;

        public CleanerSettingsViewModel(
            CleanerAutomationService automationService,
            CleanerTelemetryService telemetryService,
            CleanerStateStore? stateStore = null)
        {
            _automationService = automationService;
            _telemetryService = telemetryService;
            _stateStore = stateStore;
        }

        public IReadOnlyList<ReminderIntervalOption> ReminderOptions { get; } = new[]
        {
            new ReminderIntervalOption(1, "1 天"),
            new ReminderIntervalOption(3, "3 天"),
            new ReminderIntervalOption(7, "7 天"),
            new ReminderIntervalOption(14, "14 天")
        };

        public IReadOnlyList<ReminderIntervalOption> QuarantineRetentionOptions { get; } = new[]
        {
            new ReminderIntervalOption(3, "3 天"),
            new ReminderIntervalOption(7, "7 天"),
            new ReminderIntervalOption(14, "14 天"),
            new ReminderIntervalOption(30, "30 天")
        };

        [ObservableProperty]
        public partial bool ReminderEnabled { get; set; }

        [ObservableProperty]
        public partial bool AutoLowRiskCleanupEnabled { get; set; }

        [ObservableProperty]
        public partial int ReminderIntervalDays { get; set; } = 1;

        [ObservableProperty]
        public partial bool TelemetryEnabled { get; set; }

        [ObservableProperty]
        public partial bool AutoPurgeQuarantineEnabled { get; set; }

        [ObservableProperty]
        public partial int QuarantineRetentionDays { get; set; } = 7;

        partial void OnReminderEnabledChanged(bool value) => SaveAutomationSettings();
        partial void OnAutoLowRiskCleanupEnabledChanged(bool value) => SaveAutomationSettings();
        partial void OnReminderIntervalDaysChanged(int value) => SaveAutomationSettings();
        partial void OnTelemetryEnabledChanged(bool value) => SaveTelemetrySettings();
        partial void OnAutoPurgeQuarantineEnabledChanged(bool value) => SaveQuarantineSettings();
        partial void OnQuarantineRetentionDaysChanged(int value) => SaveQuarantineSettings();

        public async Task InitializeQuarantineSettingsAsync()
        {
            if (_stateStore == null) return;
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            _isUpdatingFromStatus = true;
            try
            {
                AutoPurgeQuarantineEnabled = preferences.AutoPurgeQuarantineEnabled;
                QuarantineRetentionDays = Math.Clamp(preferences.QuarantineRetentionDays, 1, 365);
            }
            finally
            {
                _isUpdatingFromStatus = false;
            }
        }

        private void SaveQuarantineSettings()
        {
            if (_isUpdatingFromStatus || _stateStore == null) return;
            bool enabled = AutoPurgeQuarantineEnabled;
            int days = Math.Clamp(QuarantineRetentionDays, 1, 365);
            CancellationTokenSource cts = ReplacePendingSave(ref _quarantineSaveCts);
            _ = SaveQuarantineSettingsAsync(enabled, days, cts);
        }

        private async Task SaveQuarantineSettingsAsync(bool enabled, int days, CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(350, cts.Token);
                await _stateStore!.UpdatePreferencesAsync(state =>
                {
                    state.AutoPurgeQuarantineEnabled = enabled;
                    state.QuarantineRetentionDays = days;
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WeakReferenceMessenger.Default.Send(new ShowTipMessage("隔离区设置保存失败", ex.Message));
            }
            finally
            {
                CompletePendingSave(ref _quarantineSaveCts, cts);
            }
        }

        public void UpdateFromAutomationStatus(CleanerAutomationStatus status)
        {
            _isUpdatingFromStatus = true;
            try
            {
                ReminderEnabled = status.ReminderEnabled;
                AutoLowRiskCleanupEnabled = status.AutoLowRiskCleanupEnabled;
                ReminderIntervalDays = status.ReminderIntervalDays;
            }
            finally
            {
                _isUpdatingFromStatus = false;
            }
        }

        public void UpdateFromTelemetryStatus(CleanerTelemetryStatus status)
        {
            _isUpdatingFromStatus = true;
            try
            {
                TelemetryEnabled = status.Enabled;
            }
            finally
            {
                _isUpdatingFromStatus = false;
            }
        }

        private void SaveAutomationSettings()
        {
            if (_isUpdatingFromStatus) return;
            bool reminderEnabled = ReminderEnabled;
            bool autoCleanupEnabled = AutoLowRiskCleanupEnabled;
            int intervalDays = ReminderIntervalDays;
            CancellationTokenSource cts = ReplacePendingSave(ref _automationSaveCts);
            _ = SaveAutomationSettingsAsync(reminderEnabled, autoCleanupEnabled, intervalDays, cts);
        }

        private async Task SaveAutomationSettingsAsync(
            bool reminderEnabled,
            bool autoCleanupEnabled,
            int intervalDays,
            CancellationTokenSource cts)
        {
            bool enteredGate = false;
            try
            {
                await Task.Delay(350, cts.Token);
                await _saveGate.WaitAsync(cts.Token);
                enteredGate = true;
                cts.Token.ThrowIfCancellationRequested();
                CleanerAutomationStatus status = await _automationService.SaveSettingsAsync(
                    reminderEnabled,
                    autoCleanupEnabled,
                    intervalDays);
                AutomationStatusSaved?.Invoke(status);
            }
            catch (OperationCanceledException)
            {
                // 新设置会替代仍在等待的旧设置。
            }
            catch (Exception ex)
            {
                WeakReferenceMessenger.Default.Send(
                    new ShowTipMessage("自动化设置保存失败", ex.Message));
            }
            finally
            {
                if (enteredGate) _saveGate.Release();
                CompletePendingSave(ref _automationSaveCts, cts);
            }
        }

        private void SaveTelemetrySettings()
        {
            if (_isUpdatingFromStatus) return;
            bool enabled = TelemetryEnabled;
            CancellationTokenSource cts = ReplacePendingSave(ref _telemetrySaveCts);
            _ = SaveTelemetrySettingsAsync(enabled, cts);
        }

        private async Task SaveTelemetrySettingsAsync(bool enabled, CancellationTokenSource cts)
        {
            bool enteredGate = false;
            try
            {
                await Task.Delay(350, cts.Token);
                await _saveGate.WaitAsync(cts.Token);
                enteredGate = true;
                cts.Token.ThrowIfCancellationRequested();
                var currentStatus = await _telemetryService.LoadStatusAsync();
                CleanerTelemetryStatus status = await _telemetryService.SaveSettingsAsync(enabled, currentStatus.Endpoint);
                TelemetryStatusSaved?.Invoke(status);
            }
            catch (OperationCanceledException)
            {
                // 新设置会替代仍在等待的旧设置。
            }
            catch (Exception ex)
            {
                WeakReferenceMessenger.Default.Send(
                    new ShowTipMessage("遥测设置保存失败", ex.Message));
            }
            finally
            {
                if (enteredGate) _saveGate.Release();
                CompletePendingSave(ref _telemetrySaveCts, cts);
            }
        }

        public void Shutdown()
        {
            lock (_pendingSaveSync)
            {
                _automationSaveCts?.Cancel();
                _telemetrySaveCts?.Cancel();
                _quarantineSaveCts?.Cancel();
                _automationSaveCts = null;
                _telemetrySaveCts = null;
                _quarantineSaveCts = null;
            }
        }

        private CancellationTokenSource ReplacePendingSave(ref CancellationTokenSource? field)
        {
            CancellationTokenSource next = new();
            lock (_pendingSaveSync)
            {
                field?.Cancel();
                field = next;
            }
            return next;
        }

        private void CompletePendingSave(
            ref CancellationTokenSource? field,
            CancellationTokenSource completed)
        {
            lock (_pendingSaveSync)
            {
                if (ReferenceEquals(field, completed))
                {
                    field = null;
                }
            }
            completed.Dispose();
        }
    }
}

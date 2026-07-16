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
        private bool _isUpdatingFromStatus;
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private readonly object _pendingSaveSync = new();
        private CancellationTokenSource? _automationSaveCts;
        private CancellationTokenSource? _telemetrySaveCts;

        public CleanerSettingsViewModel(
            CleanerAutomationService automationService,
            CleanerTelemetryService telemetryService)
        {
            _automationService = automationService;
            _telemetryService = telemetryService;
        }

        public IReadOnlyList<ReminderIntervalOption> ReminderOptions { get; } = new[]
        {
            new ReminderIntervalOption(1, "1 天"),
            new ReminderIntervalOption(3, "3 天"),
            new ReminderIntervalOption(7, "7 天"),
            new ReminderIntervalOption(14, "14 天")
        };

        [ObservableProperty]
        public partial bool ReminderEnabled { get; set; }

        [ObservableProperty]
        public partial bool AutoLowRiskCleanupEnabled { get; set; }

        [ObservableProperty]
        public partial int ReminderIntervalDays { get; set; } = 1;

        [ObservableProperty]
        public partial bool TelemetryEnabled { get; set; }

        partial void OnReminderEnabledChanged(bool value) => SaveAutomationSettings();
        partial void OnAutoLowRiskCleanupEnabledChanged(bool value) => SaveAutomationSettings();
        partial void OnReminderIntervalDaysChanged(int value) => SaveAutomationSettings();
        partial void OnTelemetryEnabledChanged(bool value) => SaveTelemetrySettings();

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
                await _automationService.SaveSettingsAsync(reminderEnabled, autoCleanupEnabled, intervalDays);
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
                await _telemetryService.SaveSettingsAsync(enabled, currentStatus.Endpoint);
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
                _automationSaveCts = null;
                _telemetrySaveCts = null;
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

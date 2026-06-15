using BlueSapphire.Models;
using BlueSapphire.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlueSapphire.ViewModels
{
    public record ReminderIntervalOption(int Days, string Name);

    public partial class CleanerSettingsViewModel : ObservableObject
    {
        private readonly CleanerAutomationService _automationService;
        private readonly CleanerTelemetryService _telemetryService;
        private bool _isUpdatingFromStatus;

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
        private bool _reminderEnabled;

        [ObservableProperty]
        private bool _autoLowRiskCleanupEnabled;

        [ObservableProperty]
        private int _reminderIntervalDays = 1;

        [ObservableProperty]
        private bool _telemetryEnabled;

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

        private async void SaveAutomationSettings()
        {
            if (_isUpdatingFromStatus) return;
            try
            {
                await _automationService.SaveSettingsAsync(ReminderEnabled, AutoLowRiskCleanupEnabled, ReminderIntervalDays);
            }
            catch { }
        }

        private async void SaveTelemetrySettings()
        {
            if (_isUpdatingFromStatus) return;
            try
            {
                var currentStatus = await _telemetryService.LoadStatusAsync();
                await _telemetryService.SaveSettingsAsync(TelemetryEnabled, currentStatus.Endpoint);
            }
            catch { }
        }
    }
}

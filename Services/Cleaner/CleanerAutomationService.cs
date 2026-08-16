using BlueSapphire.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services.Cleaner
{
    public sealed class CleanerAutomationService
    {
        private static readonly int[] AllowedIntervals = [1, 3, 7, 14];

        private readonly CleanerStateStore _stateStore;
        private readonly CleanerAutomationScheduleService _scheduleService;
        private readonly SemaphoreSlim _mutationGate = new(1, 1);

        public CleanerAutomationService(
            CleanerStateStore stateStore,
            CleanerAutomationScheduleService scheduleService)
        {
            _stateStore = stateStore;
            _scheduleService = scheduleService;
        }

        public async Task<CleanerAutomationStatus> LoadStatusAsync(DateTimeOffset? now = null)
        {
            CleanerPreferenceState preferences = await _stateStore.LoadPreferencesAsync();
            CleanerAutomationScheduleState scheduleState = await _scheduleService.GetStateAsync(preferences);
            return BuildStatus(preferences, now ?? DateTimeOffset.Now, scheduleState);
        }

        public async Task<CleanerAutomationStatus> SaveSettingsAsync(
            bool reminderEnabled,
            bool autoLowRiskCleanupEnabled,
            int reminderIntervalDays)
        {
            await _mutationGate.WaitAsync();
            try
            {
                CleanerPreferenceState preferences = await _stateStore.UpdatePreferencesAsync(state =>
                {
                    state.ReminderEnabled = reminderEnabled;
                    state.AutoLowRiskCleanupEnabled = autoLowRiskCleanupEnabled;
                    state.ReminderIntervalDays = NormalizeInterval(reminderIntervalDays);
                });
                CleanerAutomationScheduleState scheduleState = await _scheduleService.SyncAsync(preferences);
                preferences = await _stateStore.UpdatePreferencesAsync(
                    state => ApplyScheduleState(state, scheduleState));
                return BuildStatus(preferences, DateTimeOffset.Now, scheduleState);
            }
            finally
            {
                _mutationGate.Release();
            }
        }

        public async Task<CleanerAutomationStatus> MarkReminderHandledAsync(DateTimeOffset? handledAt = null)
        {
            DateTimeOffset timestamp = handledAt ?? DateTimeOffset.Now;
            CleanerPreferenceState preferences = await _stateStore.UpdatePreferencesAsync(
                state => state.LastReminderAt = timestamp);
            CleanerAutomationScheduleState scheduleState = await _scheduleService.GetStateAsync(preferences);
            return BuildStatus(preferences, timestamp, scheduleState);
        }

        public async Task<CleanerAutomationStatus> MarkAutoCleanupHandledAsync(DateTimeOffset? handledAt = null)
        {
            DateTimeOffset timestamp = handledAt ?? DateTimeOffset.Now;
            CleanerPreferenceState preferences = await _stateStore.UpdatePreferencesAsync(state =>
            {
                state.LastAutoCleanupAt = timestamp;
                state.LastReminderAt = timestamp;
            });
            CleanerAutomationScheduleState scheduleState = await _scheduleService.GetStateAsync(preferences);
            return BuildStatus(preferences, timestamp, scheduleState);
        }

        public static CleanerAutomationStatus BuildStatus(
            CleanerPreferenceState preferences,
            DateTimeOffset now,
            CleanerAutomationScheduleState? scheduleState = null)
        {
            int intervalDays = NormalizeInterval(preferences.ReminderIntervalDays);
            TimeSpan interval = TimeSpan.FromDays(intervalDays);

            DateTimeOffset? nextReminderAt = preferences.LastReminderAt?.Add(interval);
            DateTimeOffset? nextAutoCleanupAt = preferences.LastAutoCleanupAt?.Add(interval);

            return new CleanerAutomationStatus
            {
                ReminderEnabled = preferences.ReminderEnabled,
                AutoLowRiskCleanupEnabled = preferences.AutoLowRiskCleanupEnabled,
                ReminderIntervalDays = intervalDays,
                LastReminderAt = preferences.LastReminderAt,
                LastAutoCleanupAt = preferences.LastAutoCleanupAt,
                NextReminderAt = preferences.ReminderEnabled
                    ? nextReminderAt ?? now
                    : null,
                NextAutoCleanupAt = preferences.AutoLowRiskCleanupEnabled
                    ? nextAutoCleanupAt ?? now
                    : null,
                IsReminderDue = preferences.ReminderEnabled &&
                    (!preferences.LastReminderAt.HasValue || now - preferences.LastReminderAt.Value >= interval),
                IsAutoCleanupDue = preferences.AutoLowRiskCleanupEnabled &&
                    (!preferences.LastAutoCleanupAt.HasValue || now - preferences.LastAutoCleanupAt.Value >= interval),
                ScheduleState = scheduleState ?? new CleanerAutomationScheduleState
                {
                    IsSupported = true,
                    IsConfigured = preferences.ReminderEnabled || preferences.AutoLowRiskCleanupEnabled,
                    IsRegistered = preferences.LastAutomationScheduleRegistered,
                    TaskName = preferences.LastAutomationScheduleTaskName,
                    LastSynchronizedAt = preferences.LastAutomationScheduleSyncAt,
                    ErrorMessage = preferences.LastAutomationScheduleError
                }
            };
        }

        public static int NormalizeInterval(int reminderIntervalDays)
        {
            return AllowedIntervals.Contains(reminderIntervalDays)
                ? reminderIntervalDays
                : 7;
        }

        private static void ApplyScheduleState(CleanerPreferenceState preferences, CleanerAutomationScheduleState scheduleState)
        {
            preferences.LastAutomationScheduleSyncAt = scheduleState.LastSynchronizedAt;
            preferences.LastAutomationScheduleRegistered = scheduleState.IsRegistered;
            preferences.LastAutomationScheduleTaskName = scheduleState.TaskName;
            preferences.LastAutomationScheduleError = scheduleState.ErrorMessage;
        }
    }
}

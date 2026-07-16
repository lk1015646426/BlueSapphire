using BlueSapphire.Models;
using BlueSapphire.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Threading.Tasks;

namespace BlueSapphire.ViewModels.Cleaner
{
    public partial class CleanerAutomationViewModel : ObservableObject
    {
        private readonly CleanerAutomationService _automationService;
        
        public CleanerSettingsViewModel Settings { get; }

        private CleanerAutomationStatus _automationStatus = new();

        public CleanerAutomationStatus Status => _automationStatus;

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

        public CleanerAutomationViewModel(CleanerAutomationService automationService, CleanerSettingsViewModel settings)
        {
            _automationService = automationService;
            Settings = settings;

            Settings.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(CleanerSettingsViewModel.ReminderIntervalDays) ||
                    e.PropertyName == nameof(CleanerSettingsViewModel.ReminderEnabled) ||
                    e.PropertyName == nameof(CleanerSettingsViewModel.AutoLowRiskCleanupEnabled))
                {
                    NotifyPropertiesChanged();
                }
            };
        }

        public async Task InitializeAsync()
        {
            CleanerAutomationStatus status = await _automationService.LoadStatusAsync();
            ApplyAutomationStatus(status);
        }

        public void ApplyAutomationStatus(CleanerAutomationStatus status)
        {
            _automationStatus = status;
            Settings.UpdateFromAutomationStatus(status);
            NotifyPropertiesChanged();
        }

        private void NotifyPropertiesChanged()
        {
            OnPropertyChanged(nameof(AutomationSummaryText));
            OnPropertyChanged(nameof(AutomationModeText));
            OnPropertyChanged(nameof(AutomationNextActionText));
            OnPropertyChanged(nameof(AutomationLastActionText));
            OnPropertyChanged(nameof(AutomationHintText));
            OnPropertyChanged(nameof(AutomationScheduleText));
            OnPropertyChanged(nameof(AutomationScheduleDetailText));
        }

        [RelayCommand]
        private Task SetDailyReminder()
        {
            Settings.ReminderEnabled = true;
            Settings.ReminderIntervalDays = 1;
            return Task.CompletedTask;
        }

        [RelayCommand]
        private Task Set3DayReminder()
        {
            Settings.ReminderEnabled = true;
            Settings.ReminderIntervalDays = 3;
            return Task.CompletedTask;
        }

        [RelayCommand]
        private Task Set7DayReminder()
        {
            Settings.ReminderEnabled = true;
            Settings.ReminderIntervalDays = 7;
            return Task.CompletedTask;
        }

        [RelayCommand]
        private Task Set14DayReminder()
        {
            Settings.ReminderEnabled = true;
            Settings.ReminderIntervalDays = 14;
            return Task.CompletedTask;
        }

        [RelayCommand]
        private async Task RunAutomaticLowRiskCleanupNow()
        {
            // This needs to trigger the scan/cleanup logic in the main VM.
            // For now, we will send a message or let the main VM handle the actual cleanup logic, 
            // since automation VM shouldn't directly manage execution/progress bars if ScanVM/CleanupVM does it.
            // But wait, in the original code, this called RunCleanup with specific params?
            // Actually, we can just use WeakReferenceMessenger or an event.
            // We'll leave the implementation of RunAutomaticLowRiskCleanupNow to send a message or we just expose an event.
            // For now, let's keep it here but we'll wire it up.
            WeakReferenceMessenger.Default.Send(new BlueSapphire.Models.RunAutomaticLowRiskCleanupMessage());
            await Task.CompletedTask;
        }

        private static string FormatScheduleTime(DateTimeOffset? time)
        {
            if (time == null) return string.Empty;
            DateTimeOffset local = time.Value.ToLocalTime();
            if (local.Date == DateTime.Today) return $"今天 {local:HH:mm}";
            if (local.Date == DateTime.Today.AddDays(1)) return $"明天 {local:HH:mm}";
            if (local.Date == DateTime.Today.AddDays(-1)) return $"昨天 {local:HH:mm}";
            return local.ToString("MM-dd HH:mm");
        }
    }
}





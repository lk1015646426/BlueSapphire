using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerAutomationServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerAutomationTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadStatusAsync_ReturnsDueWhenEnabledWithoutHistory()
    {
        CleanerStateStore store = new(_rootPath);
        await store.SavePreferencesAsync(new CleanerPreferenceState
        {
            ReminderEnabled = true,
            AutoLowRiskCleanupEnabled = true,
            ReminderIntervalDays = 7
        });

        CleanerAutomationService service = new(store, CreateScheduleService());
        CleanerAutomationStatus status = await service.LoadStatusAsync(new DateTimeOffset(2026, 3, 29, 10, 0, 0, TimeSpan.Zero));

        Assert.True(status.IsReminderDue);
        Assert.True(status.IsAutoCleanupDue);
        Assert.Equal(7, status.ReminderIntervalDays);
        Assert.NotNull(status.NextReminderAt);
        Assert.NotNull(status.NextAutoCleanupAt);
    }

    [Fact]
    public async Task MarkAutoCleanupHandledAsync_UpdatesReminderAndCleanupTimestamps()
    {
        CleanerStateStore store = new(_rootPath);
        CleanerAutomationService service = new(store, CreateScheduleService());
        DateTimeOffset handledAt = new(2026, 3, 29, 12, 30, 0, TimeSpan.Zero);

        await store.SavePreferencesAsync(new CleanerPreferenceState
        {
            ReminderEnabled = true,
            AutoLowRiskCleanupEnabled = true,
            ReminderIntervalDays = 3
        });

        CleanerAutomationStatus status = await service.MarkAutoCleanupHandledAsync(handledAt);

        Assert.Equal(handledAt, status.LastAutoCleanupAt);
        Assert.Equal(handledAt, status.LastReminderAt);
        Assert.False(status.IsReminderDue);
        Assert.False(status.IsAutoCleanupDue);
        Assert.Equal(handledAt.AddDays(3), status.NextAutoCleanupAt);
    }

    [Fact]
    public void BuildStatus_NormalizesUnexpectedInterval()
    {
        CleanerAutomationStatus status = CleanerAutomationService.BuildStatus(
            new CleanerPreferenceState
            {
                ReminderEnabled = true,
                ReminderIntervalDays = 5
            },
            new DateTimeOffset(2026, 3, 29, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(7, status.ReminderIntervalDays);
    }

    [Fact]
    public async Task SaveSettingsAsync_SyncsWindowsTaskSchedulerState()
    {
        CleanerStateStore store = new(_rootPath);
        List<string> commands = new();
        CleanerAutomationScheduleService scheduleService = new(
            executablePathProvider: () => @"C:\Apps\BlueSapphire.exe",
            nowProvider: () => new DateTimeOffset(2026, 3, 29, 9, 0, 0, TimeSpan.Zero),
            commandRunner: args =>
            {
                commands.Add(args);
                return Task.FromResult(new CleanerTaskCommandResult(0, string.Empty, string.Empty));
            });

        CleanerAutomationService service = new(store, scheduleService);
        CleanerAutomationStatus status = await service.SaveSettingsAsync(true, false, 3);

        Assert.True(status.ScheduleState.IsRegistered);
        Assert.Contains(commands, command => command.Contains("/Create", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(CleanerAutomationScheduleService.DefaultTaskName, status.ScheduleState.TaskName);
    }

    private static CleanerAutomationScheduleService CreateScheduleService()
    {
        return new CleanerAutomationScheduleService(
            executablePathProvider: () => @"C:\Apps\BlueSapphire.exe",
            commandRunner: args =>
            {
                if (args.Contains("/Query", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new CleanerTaskCommandResult(0, string.Empty, string.Empty));
                }

                return Task.FromResult(new CleanerTaskCommandResult(0, string.Empty, string.Empty));
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}

using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerAutomationScheduleServiceTests
{
    [Fact]
    public async Task SyncAsync_CreatesDailyTaskWhenAutomationEnabled()
    {
        List<string> commands = new();
        CleanerAutomationScheduleService service = new(
            executablePathProvider: () => @"C:\Apps\BlueSapphire.exe",
            nowProvider: () => new DateTimeOffset(2026, 3, 29, 8, 0, 0, TimeSpan.Zero),
            commandRunner: args =>
            {
                commands.Add(args);
                return Task.FromResult(new CleanerTaskCommandResult(0, string.Empty, string.Empty));
            });

        CleanerAutomationScheduleState state = await service.SyncAsync(new CleanerPreferenceState
        {
            ReminderEnabled = true,
            ReminderIntervalDays = 14
        });

        Assert.True(state.IsRegistered);
        string createCommand = Assert.Single(commands);
        Assert.Contains("/Create", createCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/MO 14", createCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--tool=CleanerAssistant", createCommand, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncAsync_DeletesTaskWhenAutomationDisabled()
    {
        List<string> commands = new();
        CleanerAutomationScheduleService service = new(
            executablePathProvider: () => @"C:\Apps\BlueSapphire.exe",
            commandRunner: args =>
            {
                commands.Add(args);
                return Task.FromResult(new CleanerTaskCommandResult(0, string.Empty, string.Empty));
            });

        CleanerAutomationScheduleState state = await service.SyncAsync(new CleanerPreferenceState
        {
            ReminderEnabled = false,
            AutoLowRiskCleanupEnabled = false
        });

        Assert.False(state.IsRegistered);
        string deleteCommand = Assert.Single(commands);
        Assert.Contains("/Delete", deleteCommand, StringComparison.OrdinalIgnoreCase);
    }
}

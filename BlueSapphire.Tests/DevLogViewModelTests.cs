using BlueSapphire.Models;
using BlueSapphire.Services;
using BlueSapphire.ViewModels;

namespace BlueSapphire.Tests;

public sealed class DevLogViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BlueSapphireDevLogVmTests", Guid.NewGuid().ToString("N"));

    public DevLogViewModelTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task EnsureInitializedAsync_LoadsLogsAndUpdatesHud()
    {
        string seedPath = Path.Combine(_root, "missing-seed.json");
        DevLogDataService service = new(_root, seedPath);
        await service.SaveLogsAsync(
        [
            new DevLogItem
            {
                Title = "Done",
                Status = DevLogStatus.Completed,
                Timestamp = new DateTime(2026, 3, 31, 10, 0, 0)
            },
            new DevLogItem
            {
                Title = "Working",
                Status = DevLogStatus.InProgress,
                Timestamp = new DateTime(2026, 3, 30, 10, 0, 0)
            }
        ]);

        DevLogViewModel viewModel = new(service);
        await viewModel.EnsureInitializedAsync();

        Assert.Equal(2, viewModel.TotalCount);
        Assert.Equal(1, viewModel.CompletedCount);
        Assert.Equal("50%", viewModel.CompletionRate);
        Assert.Equal("Done", viewModel.Logs[0].Title);
    }

    [Fact]
    public async Task AddNewLogAsync_InsertsAtTopAndPersists()
    {
        string seedPath = Path.Combine(_root, "missing-seed.json");
        DevLogDataService service = new(_root, seedPath);
        DevLogViewModel viewModel = new(service);
        await viewModel.EnsureInitializedAsync();

        await viewModel.AddNewLogAsync("New Item", "Desc", "3.1.0", "常规迭代", "Full");

        Assert.Single(viewModel.Logs);
        Assert.Equal("New Item", viewModel.Logs[0].Title);

        List<DevLogItem> loaded = await service.LoadLogsAsync();
        Assert.Single(loaded);
        Assert.Equal("New Item", loaded[0].Title);
    }

    [Fact]
    public async Task AddNewLogAsync_DoesNothingInReadOnlyMode()
    {
        string seedPath = Path.Combine(_root, "missing-seed.json");
        DevLogDataService service = new(_root, seedPath);
        DevLogViewModel viewModel = new(service)
        {
            IsEditable = false
        };

        await viewModel.EnsureInitializedAsync();
        await viewModel.AddNewLogAsync("Blocked", "Desc", "9.9.9", "常规迭代", "Full");

        Assert.Empty(viewModel.Logs);

        List<DevLogItem> loaded = await service.LoadLogsAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task DeleteLogCommand_DoesNothingInReadOnlyMode()
    {
        string seedPath = Path.Combine(_root, "missing-seed.json");
        DevLogDataService service = new(_root, seedPath);
        await service.SaveLogsAsync(
        [
            new DevLogItem
            {
                Title = "Keep",
                Version = "1.0.0",
                Status = DevLogStatus.Completed,
                Timestamp = new DateTime(2026, 3, 31, 10, 0, 0)
            }
        ]);

        DevLogViewModel viewModel = new(service)
        {
            IsEditable = false
        };

        await viewModel.EnsureInitializedAsync();
        DevLogItem item = viewModel.Logs[0];

        await viewModel.DeleteLogCommand.ExecuteAsync(item);

        Assert.Single(viewModel.Logs);
        Assert.Equal("Keep", viewModel.Logs[0].Title);
    }

    [Fact]
    public async Task UpdateLogAsync_UpdatesExistingEntryAndReordersByTimestamp()
    {
        string seedPath = Path.Combine(_root, "missing-seed.json");
        DevLogDataService service = new(_root, seedPath);
        await service.SaveLogsAsync(
        [
            new DevLogItem
            {
                Title = "Older",
                Description = "Old",
                Version = "1.0.0",
                UpdateLevel = "常规迭代",
                Status = DevLogStatus.Completed,
                Timestamp = new DateTime(2026, 3, 1, 10, 0, 0)
            },
            new DevLogItem
            {
                Title = "Newer",
                Description = "New",
                Version = "1.0.1",
                UpdateLevel = "常规迭代",
                Status = DevLogStatus.Completed,
                Timestamp = new DateTime(2026, 3, 2, 10, 0, 0)
            }
        ]);

        DevLogViewModel viewModel = new(service);
        await viewModel.EnsureInitializedAsync();

        DevLogItem older = viewModel.Logs.Single(item => item.Version == "1.0.0");
        await viewModel.UpdateLogAsync(older, "Edited", "Updated desc", "1.0.0", "核心跃迁", "Full content", new DateTime(2026, 3, 3, 8, 0, 0));

        Assert.Equal("Edited", viewModel.Logs[0].Title);
        Assert.Equal("核心跃迁", viewModel.Logs[0].UpdateLevel);
        Assert.Equal("Full content", viewModel.Logs[0].FullContent);

        List<DevLogItem> loaded = await service.LoadLogsAsync();
        DevLogItem persisted = loaded.Single(item => item.Version == "1.0.0");
        Assert.Equal("Edited", persisted.Title);
        Assert.Equal(new DateTime(2026, 3, 3, 8, 0, 0), persisted.Timestamp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

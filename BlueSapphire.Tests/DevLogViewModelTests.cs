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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

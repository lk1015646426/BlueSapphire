using BlueSapphire.Models;
using BlueSapphire.Services;
using System.Text.Json;

namespace BlueSapphire.Tests;

public sealed class DevLogDataServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BlueSapphireDevLogTests", Guid.NewGuid().ToString("N"));
    private readonly string _seedPath;

    public DevLogDataServiceTests()
    {
        Directory.CreateDirectory(_root);
        _seedPath = Path.Combine(_root, "seed.json");
    }

    [Fact]
    public async Task LoadLogsAsync_SeedsFromConfiguredSeedFile()
    {
        List<DevLogItem> seedLogs =
        [
            new DevLogItem
            {
                Title = "Seed Item",
                Description = "From seed",
                Version = "1.0.0",
                UpdateLevel = "常规迭代",
                Timestamp = new DateTime(2026, 3, 31, 10, 0, 0)
            }
        ];

        await File.WriteAllTextAsync(_seedPath, JsonSerializer.Serialize(seedLogs));
        DevLogDataService service = new(_root, _seedPath);

        List<DevLogItem> loaded = await service.LoadLogsAsync();

        Assert.Single(loaded);
        Assert.Equal("Seed Item", loaded[0].Title);
        Assert.True(File.Exists(service.DataFilePath));
    }

    [Fact]
    public async Task SaveLogsAsync_PersistsRoundTrip()
    {
        DevLogDataService service = new(_root, _seedPath);
        List<DevLogItem> logs =
        [
            new DevLogItem
            {
                Title = "RoundTrip",
                Description = "Saved item",
                Version = "2.0.0",
                UpdateLevel = "重大升级",
                Timestamp = new DateTime(2026, 3, 31, 12, 30, 0)
            }
        ];

        await service.SaveLogsAsync(logs);
        List<DevLogItem> loaded = await service.LoadLogsAsync();

        Assert.Single(loaded);
        Assert.Equal("RoundTrip", loaded[0].Title);
        Assert.Equal("2.0.0", loaded[0].Version);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

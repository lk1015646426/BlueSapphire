using Microsoft.Extensions.Logging.Abstractions;
using BlueSapphire.Models;
using BlueSapphire.Services;
using Microsoft.Extensions.Logging;
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
        DevLogDataService service = new(NullLogger<DevLogDataService>.Instance, _root, _seedPath);

        List<DevLogItem> loaded = await service.LoadLogsAsync();

        Assert.Single(loaded);
        Assert.Equal("Seed Item", loaded[0].Title);
        Assert.True(File.Exists(service.DataFilePath));
    }

    [Fact]
    public async Task SaveLogsAsync_PersistsRoundTrip()
    {
        DevLogDataService service = new(NullLogger<DevLogDataService>.Instance, _root, _seedPath);
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

    [Fact]
    public async Task LoadLogsAsync_MergesMissingSeedVersionsIntoExistingDataFile()
    {
        List<DevLogItem> existingLogs =
        [
            new DevLogItem
            {
                Id = "seed-1",
                Title = "Existing Seed",
                Description = "Already present",
                Version = "1.0.1",
                UpdateLevel = "常规迭代",
                Timestamp = new DateTime(2026, 3, 1, 8, 0, 0)
            }
        ];

        List<DevLogItem> seedLogs =
        [
            new DevLogItem
            {
                Id = "seed-1",
                Title = "Existing Seed",
                Description = "Already present",
                Version = "1.0.1",
                UpdateLevel = "常规迭代",
                Timestamp = new DateTime(2026, 3, 1, 8, 0, 0)
            },
            new DevLogItem
            {
                Id = "seed-2",
                Title = "Missing Seed",
                Description = "Should be merged",
                Version = "1.0.2",
                UpdateLevel = "核心跃迁",
                Timestamp = new DateTime(2026, 3, 2, 8, 0, 0)
            }
        ];

        string dataPath = Path.Combine(_root, "DevMatrixLog.json");
        await File.WriteAllTextAsync(dataPath, JsonSerializer.Serialize(existingLogs));
        await File.WriteAllTextAsync(_seedPath, JsonSerializer.Serialize(seedLogs));

        DevLogDataService service = new(NullLogger<DevLogDataService>.Instance, _root, _seedPath);

        List<DevLogItem> loaded = await service.LoadLogsAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, item => item.Version == "1.0.1");
        Assert.Contains(loaded, item => item.Version == "1.0.2");

        List<DevLogItem> persisted = JsonSerializer.Deserialize<List<DevLogItem>>(await File.ReadAllTextAsync(dataPath)) ?? [];
        Assert.Equal(2, persisted.Count);
        Assert.Contains(persisted, item => item.Version == "1.0.2");
    }

    [Fact]
    public async Task LoadLogsAsync_SeedsFromVersion100WhenDataFileMissing()
    {
        List<DevLogItem> seedLogs =
        [
            new DevLogItem
            {
                Id = "seed-100",
                Title = "Initial",
                Description = "Initial release",
                Version = "1.0.0",
                UpdateLevel = "核心跃迁",
                Timestamp = new DateTime(2026, 3, 1, 8, 0, 0)
            },
            new DevLogItem
            {
                Id = "seed-101",
                Title = "Next",
                Description = "Next release",
                Version = "1.0.1",
                UpdateLevel = "常规迭代",
                Timestamp = new DateTime(2026, 3, 2, 8, 0, 0)
            }
        ];

        await File.WriteAllTextAsync(_seedPath, JsonSerializer.Serialize(seedLogs));
        DevLogDataService service = new(NullLogger<DevLogDataService>.Instance, _root, _seedPath);

        List<DevLogItem> loaded = await service.LoadLogsAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, item => item.Version == "1.0.0");
        Assert.Contains(loaded, item => item.Version == "1.0.1");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

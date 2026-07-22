using BlueSapphire.Models;
using BlueSapphire.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace BlueSapphire.Tests;

public sealed class DevLogSourceIsolationTests
{
    [Fact]
    public void SourceSeed_PreservesFormalHistoryAndRejectsKnownPollution()
    {
        string sourcePath = GetSourceSeedPath();
        List<DevLogItem> logs = JsonSerializer.Deserialize<List<DevLogItem>>(
            File.ReadAllText(sourcePath)) ?? [];

        Assert.NotEmpty(logs);
        DevLogItem formal = logs[0];
        Assert.Equal("a245943f-0512-4f4b-99af-5bd3fdcdaf5e", formal.Id);
        Assert.Equal("1.0.0", formal.Version);
        Assert.Equal("Keep", formal.Title);
        Assert.Equal(string.Empty, formal.Description);
        Assert.Equal(string.Empty, formal.FullContent);
        Assert.DoesNotContain(logs, item => item.Id is "seed-100" or "seed-101");
    }

    [Fact]
    public async Task SaveLogsAsync_WritesRuntimeCopyWithoutChangingSourceSeed()
    {
        string sourcePath = GetSourceSeedPath();
        string sourceBefore = await File.ReadAllTextAsync(sourcePath);
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "BlueSapphireDevLogIsolationTests",
            Guid.NewGuid().ToString("N"));
        string runtimeRoot = Path.Combine(testRoot, "runtime");
        string seedCopy = Path.Combine(testRoot, "seed.json");

        Directory.CreateDirectory(testRoot);
        await File.WriteAllTextAsync(seedCopy, sourceBefore);

        try
        {
            DevLogDataService service = new(
                NullLogger<DevLogDataService>.Instance,
                runtimeRoot,
                seedCopy);

            List<DevLogItem> runtimeLogs = await service.LoadLogsAsync();
            runtimeLogs.Add(new DevLogItem
            {
                Id = "runtime-only",
                Title = "Runtime only",
                Description = "Must not be copied into project Assets",
                FullContent = string.Empty,
                Version = "runtime-test",
                UpdateLevel = "常规迭代",
                Status = DevLogStatus.Completed,
                Timestamp = new DateTime(2026, 7, 22, 12, 0, 0)
            });

            await service.SaveLogsAsync(runtimeLogs);

            Assert.True(File.Exists(service.DataFilePath));
            Assert.Contains("runtime-only", await File.ReadAllTextAsync(service.DataFilePath));
            Assert.Equal(sourceBefore, await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static string GetSourceSeedPath()
    {
        return Path.Combine(FindProjectRoot(), "Assets", "DevMatrixLog.json");
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BlueSapphire.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到 BlueSapphire 项目根目录。");
    }
}

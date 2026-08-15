using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class AISharedContextServiceTests
{
    [Fact]
    public void CleanerScan_IsClonedAndExpiresByRequestedAge()
    {
        var service = new AISharedContextService();
        var report = new CleanerScanReport
        {
            CreatedAt = DateTimeOffset.Now,
            Scope = CleanerScanScope.Quick,
            Items =
            [
                new CleanerScanItem
                {
                    Name = "缓存",
                    Path = @"C:\Temp",
                    RiskLevel = CleanerRiskLevel.Low,
                    CanSelect = true
                }
            ]
        };

        service.SetCleanerScan(report);
        report.Items[0].Name = "外部修改";

        CleanerScanReport cloned = service.GetCleanerScan(TimeSpan.FromMinutes(1))!;
        Assert.Equal("缓存", cloned.Items[0].Name);
        cloned.Items[0].Name = "再次修改";
        Assert.Equal("缓存", service.GetCleanerScan()!.Items[0].Name);
        Assert.Null(service.GetCleanerScan(TimeSpan.Zero));
    }

    [Fact]
    public async Task ParallelSetAndGet_ConsistentSnapshotsWithoutCorruption()
    {
        var service = new AISharedContextService();
        const int iterations = 200;

        Task[] writers = Enumerable.Range(0, 4).Select(writerIndex => Task.Run(async () =>
        {
            for (int i = 0; i < iterations; i++)
            {
                var report = new CleanerScanReport
                {
                    CreatedAt = DateTimeOffset.Now,
                    Items =
                    [
                        new CleanerScanItem
                        {
                            Name = $"w{writerIndex}-i{i}",
                            Path = @"C:\Temp",
                            RiskLevel = CleanerRiskLevel.Low,
                            CanSelect = true
                        }
                    ]
                };
                service.SetCleanerScan(report);
                service.SetCurrentMediaFolder($@"C:\Media\w{writerIndex}\{i}");
                await Task.Yield();
            }
        })).ToArray();

        Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < iterations; i++)
            {
                CleanerScanReport? snapshot = service.GetCleanerScan();
                if (snapshot != null)
                {
                    // 读到的每个快照必须完整可读：单条目且名字与路径非空。
                    CleanerScanItem item = Assert.Single(snapshot.Items);
                    Assert.False(string.IsNullOrEmpty(item.Name));
                }

                string? folder = service.GetCurrentMediaFolder();
                Assert.False(folder != null && folder.Contains('\0'));
                await Task.Yield();
            }
        })).ToArray();

        await Task.WhenAll(writers.Concat(readers));
    }
}

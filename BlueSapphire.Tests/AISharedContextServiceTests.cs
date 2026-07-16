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
}

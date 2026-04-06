using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerAuditServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerAuditTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RecordOperations_UpdatesAuditSnapshot()
    {
        CleanerStateStore store = new(_rootPath);
        CleanerAuditService service = new(store);

        CleanerScanReport report = new()
        {
            CreatedAt = new DateTimeOffset(2026, 3, 29, 10, 0, 0, TimeSpan.Zero),
            Scope = CleanerScanScope.Deep,
            Duration = TimeSpan.FromSeconds(2),
            AnalysisDriveRoots = [@"C:\", @"D:\"],
            UsedIncrementalReuse = true,
            ReusedItemCount = 2,
            Items =
            [
                new CleanerScanItem { RuleId = "user_temp", RiskLevel = CleanerRiskLevel.Low, ExecutionMode = CleanerExecutionMode.Quarantine, SizeBytes = 2048 },
                new CleanerScanItem { RuleId = "user_temp", RiskLevel = CleanerRiskLevel.Low, ExecutionMode = CleanerExecutionMode.Quarantine, SizeBytes = 1024 },
                new CleanerScanItem { RuleId = "windows_temp", RiskLevel = CleanerRiskLevel.High, ExecutionMode = CleanerExecutionMode.None, ViewOnly = true, SizeBytes = 4096 }
            ]
        };

        CleanerCleanupBatch cleanupBatch = new()
        {
            ReleasedBytes = 1024,
            Entries =
            [
                new CleanerCleanupEntry
                {
                    RuleId = "user_temp",
                    Status = "Completed",
                    SizeBytes = 1024
                },
                new CleanerCleanupEntry
                {
                    RuleId = "windows_temp",
                    Status = "Failed",
                    FailureReason = CleanerFailureReason.InUse
                }
            ]
        };

        await service.RecordScanAsync(report);
        await service.RecordDeselectionAsync(
        [
            new CleanerScanItem
            {
                RuleId = "user_temp",
                DefaultSelected = true,
                CanSelect = true,
                IsSelected = false,
                RiskLevel = CleanerRiskLevel.Low,
                ExecutionMode = CleanerExecutionMode.Quarantine
            }
        ]);
        await service.RecordCleanupAsync(cleanupBatch, manualDeselections: 2);
        await service.RecordRestoreAsync(new CleanerRestoreSummary
        {
            RestoredCount = 1,
            RestoredBytes = 512
        });
        await service.RecordRetryAsync(
            retryAttempts: 1,
            retryRecoveredItems: 1,
            failedEntries:
            [
                new CleanerCleanupEntry
                {
                    RuleId = "windows_temp",
                    Status = "Failed",
                    FailureReason = CleanerFailureReason.AccessDenied
                }
            ]);

        CleanerAuditSnapshot snapshot = await service.LoadSnapshotAsync();

        Assert.Equal(1, snapshot.TotalScans);
        Assert.Equal(2000, snapshot.LastScanDurationMs);
        Assert.Equal(3, snapshot.LastScanItemCount);
        Assert.Equal(1, snapshot.TotalCleanupRuns);
        Assert.Equal(1024, snapshot.TotalReleasedBytes);
        Assert.Equal(1, snapshot.TotalCleanupFailures);
        Assert.Equal(1, snapshot.TotalRestoredItems);
        Assert.Equal(512, snapshot.TotalRestoredBytes);
        Assert.Equal(1, snapshot.TotalRetryRuns);
        Assert.Equal(1, snapshot.TotalRetryRecoveredItems);
        Assert.Equal(2, snapshot.TotalManualDeselections);
        Assert.Equal(2, snapshot.RuleHits["user_temp"]);
        Assert.Equal(1, snapshot.RuleHits["windows_temp"]);
        Assert.Equal(1, snapshot.RuleCleanupSuccesses["user_temp"]);
        Assert.Equal(1, snapshot.RuleDeselections["user_temp"]);
        Assert.Equal(2, snapshot.RuleFailures["windows_temp"]);
        Assert.Equal(1, snapshot.FailureReasons["被占用"]);
        Assert.Equal(1, snapshot.FailureReasons["权限不足"]);
        Assert.Single(snapshot.RecentScans);
        Assert.Equal(CleanerScanScope.Deep, snapshot.RecentScans[0].Scope);
        Assert.Equal(7168, snapshot.RecentScans[0].TotalBytes);
        Assert.Equal(3072, snapshot.RecentScans[0].SafeBytes);
        Assert.Equal(4096, snapshot.RecentScans[0].ViewOnlyBytes);
        Assert.Equal(2, snapshot.RecentScans[0].ReusedItemCount);
    }

    [Fact]
    public async Task ExportDiagnosticReportAsync_WritesJsonReport()
    {
        CleanerStateStore store = new(_rootPath);
        CleanerAuditService service = new(store);

        await store.SaveAuditAsync(new CleanerAuditSnapshot
        {
            RuleHits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["cache_rule"] = 4
            },
            RuleFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["cache_rule"] = 2
            },
            RuleDeselections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["cache_rule"] = 1
            }
        });
        await store.SaveRuleUpdateStateAsync(new CleanerRuleUpdateState
        {
            LocalDisabledRuleIds = ["cache_rule"]
        });

        string reportPath = await service.ExportDiagnosticReportAsync(
        [
            new CleanerRuleDefinition
            {
                Id = "cache_rule",
                Name = "缓存规则"
            }
        ],
        new CleanerRuleBundleStatus
        {
            EffectiveRuleCount = 3,
            LocalDisabledRuleCount = 1
        });

        Assert.True(File.Exists(reportPath));
        string json = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("缓存规则", json);
        Assert.Contains("LocalDisabledRuleCount", json);
    }

    [Fact]
    public async Task RecordScanAsync_KeepsLatestTwelveSnapshots()
    {
        CleanerStateStore store = new(_rootPath);
        CleanerAuditService service = new(store);

        for (int index = 0; index < 15; index++)
        {
            await service.RecordScanAsync(new CleanerScanReport
            {
                CreatedAt = new DateTimeOffset(2026, 3, 29, 10, 0, 0, TimeSpan.Zero).AddMinutes(index),
                Scope = CleanerScanScope.Quick,
                Duration = TimeSpan.FromSeconds(1),
                Items =
                [
                    new CleanerScanItem
                    {
                        RuleId = "user_temp",
                        RiskLevel = CleanerRiskLevel.Low,
                        ExecutionMode = CleanerExecutionMode.Quarantine,
                        SizeBytes = 1024 + index
                    }
                ]
            });
        }

        CleanerAuditSnapshot snapshot = await service.LoadSnapshotAsync();

        Assert.Equal(12, snapshot.RecentScans.Count);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 10, 14, 0, TimeSpan.Zero), snapshot.RecentScans[0].CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 10, 3, 0, TimeSpan.Zero), snapshot.RecentScans[^1].CreatedAt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}

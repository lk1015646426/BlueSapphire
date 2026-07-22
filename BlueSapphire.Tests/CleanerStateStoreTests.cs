using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerStateStoreTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoadHistory_RoundTripsBatches()
    {
        CleanerStateStore store = new(_rootPath);
        List<CleanerCleanupBatch> history =
        [
            new CleanerCleanupBatch
            {
                BatchId = "batch-1",
                ReleasedBytes = 1024,
                Entries =
                [
                    new CleanerCleanupEntry
                    {
                        OriginalPath = @"C:\Temp\a.tmp",
                        Status = "Completed"
                    }
                ]
            }
        ];

        await store.SaveHistoryAsync(history);
        IReadOnlyList<CleanerCleanupBatch> loaded = await store.LoadHistoryAsync();

        Assert.Single(loaded);
        Assert.Equal("batch-1", loaded[0].BatchId);
        Assert.Single(loaded[0].Entries);
    }

    [Fact]
    public async Task SaveAndLoadPreferences_RoundTripsDriveSelection()
    {
        CleanerStateStore store = new(_rootPath);
        CleanerPreferenceState preferences = new()
        {
            SelectedDriveRoots = [@"C:\", @"D:\"],
            ReminderEnabled = true,
            AutoLowRiskCleanupEnabled = true,
            ReminderIntervalDays = 3,
            LastReminderAt = DateTimeOffset.Now.AddDays(-3),
            LastAutoCleanupAt = DateTimeOffset.Now.AddDays(-1),
            AutoPurgeQuarantineEnabled = true,
            QuarantineRetentionDays = 14
        };

        await store.SavePreferencesAsync(preferences);
        CleanerPreferenceState loaded = await store.LoadPreferencesAsync();

        Assert.Equal(2, loaded.SelectedDriveRoots.Count);
        Assert.Contains(@"C:\", loaded.SelectedDriveRoots);
        Assert.Contains(@"D:\", loaded.SelectedDriveRoots);
        Assert.True(loaded.ReminderEnabled);
        Assert.True(loaded.AutoLowRiskCleanupEnabled);
        Assert.Equal(3, loaded.ReminderIntervalDays);
        Assert.NotNull(loaded.LastReminderAt);
        Assert.NotNull(loaded.LastAutoCleanupAt);
        Assert.True(loaded.AutoPurgeQuarantineEnabled);
        Assert.Equal(14, loaded.QuarantineRetentionDays);
    }

    [Fact]
    public async Task UpdatePreferencesAsync_PreservesConcurrentFeatureChanges()
    {
        CleanerStateStore store = new(_rootPath);

        await Task.WhenAll(
            store.UpdatePreferencesAsync(state =>
            {
                state.TelemetryEnabled = true;
                state.TelemetryEndpoint = "https://example.com/telemetry";
            }),
            store.UpdatePreferencesAsync(state =>
            {
                state.ReminderEnabled = true;
                state.ReminderIntervalDays = 7;
            }),
            store.UpdatePreferencesAsync(state =>
            {
                state.SelectedDriveRoots = [@"C:\", @"D:\"];
            }));

        CleanerPreferenceState loaded = await store.LoadPreferencesAsync();
        Assert.True(loaded.TelemetryEnabled);
        Assert.True(loaded.ReminderEnabled);
        Assert.Equal(7, loaded.ReminderIntervalDays);
        Assert.Equal(2, loaded.SelectedDriveRoots.Count);
    }

    [Fact]
    public async Task SaveAndLoadRuleUpdateState_RoundTripsRemoteMetadata()
    {
        CleanerStateStore store = new(_rootPath);
        CleanerRuleUpdateState state = new()
        {
            RemoteUri = "https://example.com/cleaner-rules.json",
            LastBundleVersion = "2026.03.29",
            LastBundleSource = "远程热更新",
            LastRefreshedAt = DateTimeOffset.Now,
            LocalDisabledRuleIds = ["rule_a", "rule_b"]
        };

        await store.SaveRuleUpdateStateAsync(state);
        CleanerRuleUpdateState loaded = await store.LoadRuleUpdateStateAsync();

        Assert.Equal(state.RemoteUri, loaded.RemoteUri);
        Assert.Equal(state.LastBundleVersion, loaded.LastBundleVersion);
        Assert.Equal(state.LastBundleSource, loaded.LastBundleSource);
        Assert.NotNull(loaded.LastRefreshedAt);
        Assert.Equal(2, loaded.LocalDisabledRuleIds.Count);
    }

    [Fact]
    public async Task SaveAndLoadAudit_RoundTripsRecentScanSnapshots()
    {
        CleanerStateStore store = new(_rootPath);
        CleanerAuditSnapshot snapshot = new()
        {
            TotalScans = 3,
            RecentScans =
            [
                new CleanerScanSnapshot
                {
                    Scope = CleanerScanScope.Deep,
                    DriveRoots = [@"C:\", @"D:\"],
                    TotalItemCount = 12,
                    TotalBytes = 1024,
                    UsedIncrementalReuse = true,
                    ReusedItemCount = 4
                }
            ]
        };

        await store.SaveAuditAsync(snapshot);
        CleanerAuditSnapshot loaded = await store.LoadAuditAsync();

        Assert.Equal(3, loaded.TotalScans);
        Assert.Single(loaded.RecentScans);
        Assert.Equal(CleanerScanScope.Deep, loaded.RecentScans[0].Scope);
        Assert.Equal(4, loaded.RecentScans[0].ReusedItemCount);
        Assert.Contains(@"D:\", loaded.RecentScans[0].DriveRoots);
    }

    [Fact]
    public async Task LoadHistoryAsync_MigratesLegacyReleasedBytesFromEntryModes()
    {
        CleanerStateStore store = new(_rootPath);
        await store.SaveHistoryAsync(
        [
            new CleanerCleanupBatch
            {
                AccountingVersion = 0,
                BatchId = "legacy",
                ReleasedBytes = 300,
                Entries =
                [
                    new CleanerCleanupEntry
                    {
                        SizeBytes = 100,
                        ExecutionMode = CleanerExecutionMode.Permanent,
                        Status = "Completed"
                    },
                    new CleanerCleanupEntry
                    {
                        SizeBytes = 200,
                        ExecutionMode = CleanerExecutionMode.Quarantine,
                        Status = "Completed"
                    }
                ]
            }
        ]);

        CleanerCleanupBatch migrated = Assert.Single(await store.LoadHistoryAsync());

        Assert.Equal(CleanerExecutionService.CurrentAccountingVersion, migrated.AccountingVersion);
        Assert.Equal(300, migrated.ProcessedBytes);
        Assert.Equal(100, migrated.ReleasedBytes);
        Assert.Equal(200, migrated.RecoverableBytes);

        CleanerCleanupBatch persisted = Assert.Single(await store.LoadHistoryAsync());
        Assert.Equal(CleanerExecutionService.CurrentAccountingVersion, persisted.AccountingVersion);
    }

    [Fact]
    public async Task LoadAuditAsync_RebuildsLegacyReleasedTotalFromMigratedHistory()
    {
        CleanerStateStore store = new(_rootPath);
        await store.SaveHistoryAsync(
        [
            new CleanerCleanupBatch
            {
                AccountingVersion = 0,
                ReleasedBytes = 900,
                Entries =
                [
                    new CleanerCleanupEntry
                    {
                        SizeBytes = 150,
                        ExecutionMode = CleanerExecutionMode.Permanent,
                        Status = "Completed"
                    },
                    new CleanerCleanupEntry
                    {
                        SizeBytes = 750,
                        ExecutionMode = CleanerExecutionMode.Quarantine,
                        Status = "Completed"
                    }
                ]
            }
        ]);
        await store.SaveAuditAsync(new CleanerAuditSnapshot
        {
            AccountingVersion = 0,
            TotalCleanupRuns = 1,
            TotalReleasedBytes = 900
        });

        CleanerAuditSnapshot migrated = await store.LoadAuditAsync();

        Assert.Equal(CleanerExecutionService.CurrentAccountingVersion, migrated.AccountingVersion);
        Assert.Equal(150, migrated.TotalReleasedBytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}

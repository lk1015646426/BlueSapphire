using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerExecutionServiceTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerExec", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAndRestore_QuarantineFlowWorks()
    {
        Directory.CreateDirectory(_workspace);
        string sourceFolder = Path.Combine(_workspace, "Temp");
        Directory.CreateDirectory(sourceFolder);
        string sourceFile = Path.Combine(sourceFolder, "cache.bin");
        await File.WriteAllTextAsync(sourceFile, "temporary data");

        CleanerStateStore store = new(Path.Combine(_workspace, "State"));
        CleanerExecutionService service = new(
            new NativeFileService(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService(),
            new CleanerBoundaryGuard());

        CleanerScanItem item = new()
        {
            Name = "Temp Cache",
            Path = sourceFolder,
            ScanKind = CleanerScanKind.DirectoryContents,
            ExecutionMode = CleanerExecutionMode.Quarantine,
            RiskLevel = CleanerRiskLevel.Low,
            CanSelect = true,
            IsSelected = true
        };

        CleanerCleanupBatch batch = await service.ExecuteAsync([item], CleanerScanScope.Quick, null, CancellationToken.None);

        Assert.Single(batch.Entries);
        Assert.False(File.Exists(sourceFile));
        Assert.True(File.Exists(batch.Entries[0].BackupPath));

        CleanerRestoreSummary restore = await service.RestoreLatestAsync(CancellationToken.None);

        Assert.Equal(1, restore.RestoredCount);
        Assert.True(File.Exists(sourceFile));
    }

    [Fact]
    public async Task RestoreEntryAsync_RestoresSingleCleanupEntry()
    {
        Directory.CreateDirectory(_workspace);
        string sourceFolder = Path.Combine(_workspace, "TempSingle");
        Directory.CreateDirectory(sourceFolder);
        string firstFile = Path.Combine(sourceFolder, "a.tmp");
        string secondFile = Path.Combine(sourceFolder, "b.tmp");
        await File.WriteAllTextAsync(firstFile, "a");
        await File.WriteAllTextAsync(secondFile, "b");

        CleanerStateStore store = new(Path.Combine(_workspace, "StateSingle"));
        CleanerExecutionService service = new(
            new NativeFileService(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService(),
            new CleanerBoundaryGuard());

        CleanerScanItem item = new()
        {
            Name = "Temp Cache",
            Path = sourceFolder,
            ScanKind = CleanerScanKind.DirectoryContents,
            ExecutionMode = CleanerExecutionMode.Quarantine,
            RiskLevel = CleanerRiskLevel.Low,
            CanSelect = true,
            IsSelected = true
        };

        CleanerCleanupBatch batch = await service.ExecuteAsync([item], CleanerScanScope.Quick, null, CancellationToken.None);
        CleanerCleanupEntry entry = Assert.Single(
            batch.Entries,
            candidate => candidate.OriginalPath.EndsWith("a.tmp", StringComparison.OrdinalIgnoreCase));

        CleanerRestoreSummary restore = await service.RestoreEntryAsync(batch.BatchId, entry.EntryId, CancellationToken.None);

        Assert.Equal(1, restore.RestoredCount);
        Assert.True(File.Exists(firstFile));
        Assert.False(File.Exists(secondFile));
    }

    [Fact]
    public async Task RetryFailedEntriesAsync_RetriesRecoverableEntry()
    {
        Directory.CreateDirectory(_workspace);
        string sourceFolder = Path.Combine(_workspace, "RetryTarget");
        Directory.CreateDirectory(sourceFolder);
        string sourceFile = Path.Combine(sourceFolder, "retry.tmp");
        await File.WriteAllTextAsync(sourceFile, "retry payload");

        CleanerStateStore store = new(Path.Combine(_workspace, "StateRetry"));
        CleanerExecutionService service = new(
            new NativeFileService(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService(),
            new CleanerBoundaryGuard());

        CleanerCleanupBatch historyBatch = new()
        {
            BatchId = "retry-batch",
            Entries =
            [
                new CleanerCleanupEntry
                {
                    EntryId = "retry-entry",
                    ItemName = "Retry Target",
                    OriginalPath = sourceFile,
                    SizeBytes = new FileInfo(sourceFile).Length,
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Medium,
                    Status = "Failed",
                    FailureReason = CleanerFailureReason.InUse,
                    ErrorMessage = "mock in use",
                    CanRestore = true
                }
            ]
        };

        await store.SaveHistoryAsync([historyBatch]);

        CleanerCleanupBatch? retried = await service.RetryFailedEntriesAsync("retry-batch", null, CancellationToken.None);

        Assert.NotNull(retried);
        CleanerCleanupEntry entry = Assert.Single(retried!.Entries);
        Assert.Equal("Completed", entry.Status);
        Assert.Equal(CleanerFailureReason.None, entry.FailureReason);
        Assert.False(string.IsNullOrWhiteSpace(entry.BackupPath));
        Assert.False(File.Exists(sourceFile));
        Assert.True(File.Exists(entry.BackupPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, true);
        }
    }
}

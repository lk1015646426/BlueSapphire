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
        Assert.Equal(0, batch.ReleasedBytes);
        Assert.Equal("temporary data".Length, batch.RecoverableBytes);
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
    public async Task RestoreBatchAsync_RestoresTheExplicitlySelectedOlderBatch()
    {
        Directory.CreateDirectory(_workspace);
        CleanerStateStore store = new(Path.Combine(_workspace, "StateSelectedBatch"));
        string newestBackup = Path.Combine(store.QuarantineRootPath, "newest", "new.tmp");
        string olderBackup = Path.Combine(store.QuarantineRootPath, "older", "old.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(newestBackup)!);
        Directory.CreateDirectory(Path.GetDirectoryName(olderBackup)!);
        await File.WriteAllTextAsync(newestBackup, "new");
        await File.WriteAllTextAsync(olderBackup, "old");
        string newestOriginal = Path.Combine(_workspace, "new.tmp");
        string olderOriginal = Path.Combine(_workspace, "old.tmp");
        await store.SaveHistoryAsync(
        [
            new CleanerCleanupBatch
            {
                BatchId = "newest",
                Entries = [CreateRestorableEntry("new", newestOriginal, newestBackup, 3)]
            },
            new CleanerCleanupBatch
            {
                BatchId = "older",
                Entries = [CreateRestorableEntry("old", olderOriginal, olderBackup, 3)]
            }
        ]);
        CleanerExecutionService service = CreateService(store);

        CleanerRestoreSummary summary = await service.RestoreBatchAsync("older", null, CancellationToken.None);

        Assert.Equal(1, summary.RestoredCount);
        Assert.True(File.Exists(olderOriginal));
        Assert.False(File.Exists(newestOriginal));
        IReadOnlyList<CleanerCleanupBatch> saved = await store.LoadHistoryAsync();
        Assert.False(saved[0].Entries[0].Restored);
        Assert.True(saved[1].Entries[0].Restored);
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

    [Fact]
    public async Task ExecuteAsync_UsesPerTargetSizesAndSeparatesRecoverableBytes()
    {
        Directory.CreateDirectory(_workspace);
        string sourceFolder = Path.Combine(_workspace, "MultipleTargets");
        string firstFolder = Path.Combine(sourceFolder, "A");
        string secondFolder = Path.Combine(sourceFolder, "B");
        Directory.CreateDirectory(firstFolder);
        Directory.CreateDirectory(secondFolder);
        await File.WriteAllTextAsync(Path.Combine(firstFolder, "a.bin"), "abc");
        await File.WriteAllTextAsync(Path.Combine(secondFolder, "b.bin"), "12345");

        CleanerStateStore store = new(Path.Combine(_workspace, "StateMultiple"));
        CleanerExecutionService service = new(
            new NativeFileService(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService(),
            new CleanerBoundaryGuard());

        CleanerScanItem item = new()
        {
            Name = "Multiple Targets",
            Path = sourceFolder,
            SizeBytes = 999,
            ScanKind = CleanerScanKind.DirectoryContents,
            ExecutionMode = CleanerExecutionMode.Quarantine,
            RiskLevel = CleanerRiskLevel.Low,
            CanSelect = true,
            IsSelected = true
        };

        CleanerCleanupBatch batch = await service.ExecuteAsync(
            [item],
            CleanerScanScope.Quick,
            null,
            CancellationToken.None);

        Assert.Equal(2, batch.Entries.Count);
        Assert.Equal(8, batch.ProcessedBytes);
        Assert.Equal(8, batch.RecoverableBytes);
        Assert.Equal(0, batch.ReleasedBytes);
    }

    [Fact]
    public async Task ExecuteAsync_DeduplicatesOverlappingTargetsAcrossItems()
    {
        Directory.CreateDirectory(_workspace);
        string sourceFile = Path.Combine(_workspace, "duplicate-target.tmp");
        await File.WriteAllTextAsync(sourceFile, "duplicate");
        CleanerStateStore store = new(Path.Combine(_workspace, "StateDuplicateTarget"));
        CleanerExecutionService service = CreateService(store);
        CleanerScanItem first = new()
        {
            ObjectId = "first",
            Name = "First",
            Path = sourceFile,
            TargetPaths = [sourceFile],
            ScanKind = CleanerScanKind.File,
            ExecutionMode = CleanerExecutionMode.Quarantine,
            RiskLevel = CleanerRiskLevel.Low,
            CanSelect = true,
            IsSelected = true
        };
        CleanerScanItem second = new()
        {
            ObjectId = "second",
            Name = "Second",
            Path = sourceFile,
            TargetPaths = [sourceFile],
            ScanKind = CleanerScanKind.File,
            ExecutionMode = CleanerExecutionMode.Quarantine,
            RiskLevel = CleanerRiskLevel.Low,
            CanSelect = true,
            IsSelected = true
        };

        CleanerCleanupBatch batch = await service.ExecuteAsync(
            [first, second], CleanerScanScope.Quick, null, CancellationToken.None);

        Assert.Single(batch.Entries);
        Assert.Equal("Completed", batch.Entries[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_CountsPermanentDeletionAsReleasedSpace()
    {
        Directory.CreateDirectory(_workspace);
        string sourceFile = Path.Combine(_workspace, "permanent.tmp");
        await File.WriteAllTextAsync(sourceFile, "release me");

        CleanerStateStore store = new(Path.Combine(_workspace, "StatePermanent"));
        CleanerExecutionService service = new(
            new NativeFileService(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService(),
            new CleanerBoundaryGuard());

        CleanerScanItem item = new()
        {
            Name = "Permanent Cache",
            Path = sourceFile,
            ScanKind = CleanerScanKind.File,
            ExecutionMode = CleanerExecutionMode.Permanent,
            RiskLevel = CleanerRiskLevel.Low,
            CanSelect = true,
            IsSelected = true
        };

        CleanerCleanupBatch batch = await service.ExecuteAsync(
            [item],
            CleanerScanScope.Quick,
            null,
            CancellationToken.None);

        Assert.Equal("release me".Length, batch.ProcessedBytes);
        Assert.Equal("release me".Length, batch.ReleasedBytes);
        Assert.Equal("release me".Length, batch.ReleasedBytesByDrive[Path.GetPathRoot(sourceFile)!]);
        Assert.Equal(0, batch.RecoverableBytes);
        Assert.False(File.Exists(sourceFile));
    }

    [Fact]
    public async Task PurgeQuarantineAsync_PermanentlyDeletesBackupsAndUpdatesHistory()
    {
        Directory.CreateDirectory(_workspace);
        string sourceFolder = Path.Combine(_workspace, "PurgeTarget");
        Directory.CreateDirectory(sourceFolder);
        string sourceFile = Path.Combine(sourceFolder, "purge.tmp");
        await File.WriteAllTextAsync(sourceFile, "purge payload");

        CleanerStateStore store = new(Path.Combine(_workspace, "StatePurge"));
        CleanerExecutionService service = new(
            new NativeFileService(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService(),
            new CleanerBoundaryGuard());

        CleanerCleanupBatch batch = await service.ExecuteAsync(
            [new CleanerScanItem
            {
                Name = "Purge Cache",
                Path = sourceFile,
                ScanKind = CleanerScanKind.File,
                ExecutionMode = CleanerExecutionMode.Quarantine,
                RiskLevel = CleanerRiskLevel.Low,
                CanSelect = true,
                IsSelected = true
            }],
            CleanerScanScope.Quick,
            null,
            CancellationToken.None);

        string backupPath = Assert.Single(batch.Entries).BackupPath;
        Assert.True(File.Exists(backupPath));

        CleanerQuarantinePurgeSummary summary = await service.PurgeQuarantineAsync(CancellationToken.None);

        Assert.Equal(1, summary.PurgedCount);
        Assert.Equal("purge payload".Length, summary.ReleasedBytes);
        Assert.Equal("purge payload".Length, summary.ReleasedBytesByDrive[Path.GetPathRoot(backupPath)!]);
        Assert.False(File.Exists(backupPath));

        CleanerCleanupEntry savedEntry = Assert.Single(Assert.Single(await store.LoadHistoryAsync()).Entries);
        Assert.False(savedEntry.CanRestore);
        Assert.Equal("Purged", savedEntry.Status);
        Assert.Equal(string.Empty, savedEntry.BackupPath);
    }

    [Fact]
    public async Task PurgeExpiredQuarantineAsync_DeletesOnlyBatchesPastRetention()
    {
        Directory.CreateDirectory(_workspace);
        CleanerStateStore store = new(Path.Combine(_workspace, "StateRetention"));
        string oldBackup = Path.Combine(store.QuarantineRootPath, "old", "old.cache");
        string freshBackup = Path.Combine(store.QuarantineRootPath, "fresh", "fresh.cache");
        Directory.CreateDirectory(Path.GetDirectoryName(oldBackup)!);
        Directory.CreateDirectory(Path.GetDirectoryName(freshBackup)!);
        await File.WriteAllTextAsync(oldBackup, "old");
        await File.WriteAllTextAsync(freshBackup, "fresh");
        await store.SaveHistoryAsync(
        [
            new CleanerCleanupBatch
            {
                AccountingVersion = CleanerExecutionService.CurrentAccountingVersion,
                BatchId = "fresh",
                CreatedAt = DateTimeOffset.Now.AddDays(-2),
                Entries =
                [
                    new CleanerCleanupEntry
                    {
                        BackupPath = freshBackup,
                        SizeBytes = 5,
                        ExecutionMode = CleanerExecutionMode.Quarantine,
                        Status = "Completed",
                        CanRestore = true
                    }
                ]
            },
            new CleanerCleanupBatch
            {
                AccountingVersion = CleanerExecutionService.CurrentAccountingVersion,
                BatchId = "old",
                CreatedAt = DateTimeOffset.Now.AddDays(-10),
                Entries =
                [
                    new CleanerCleanupEntry
                    {
                        BackupPath = oldBackup,
                        SizeBytes = 3,
                        ExecutionMode = CleanerExecutionMode.Quarantine,
                        Status = "Completed",
                        CanRestore = true
                    }
                ]
            }
        ]);
        CleanerExecutionService service = new(
            new NativeFileService(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService(),
            new CleanerBoundaryGuard());

        CleanerQuarantinePurgeSummary summary = await service.PurgeExpiredQuarantineAsync(7, CancellationToken.None);

        Assert.Equal(1, summary.PurgedCount);
        Assert.Equal(3, summary.ReleasedBytes);
        Assert.False(File.Exists(oldBackup));
        Assert.True(File.Exists(freshBackup));
    }

    [Fact]
    public async Task RestoreLatestAsync_CancellationPersistsEntriesRestoredBeforeCancellation()
    {
        Directory.CreateDirectory(_workspace);
        CleanerStateStore store = new(Path.Combine(_workspace, "StateRestoreCancellation"));
        string firstBackup = Path.Combine(store.QuarantineRootPath, "restore-cancel", "first.tmp");
        string secondBackup = Path.Combine(store.QuarantineRootPath, "restore-cancel", "second.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(firstBackup)!);
        await File.WriteAllTextAsync(firstBackup, "first");
        await File.WriteAllTextAsync(secondBackup, "second");
        string firstOriginal = Path.Combine(_workspace, "RestoreCancellation", "first.tmp");
        string secondOriginal = Path.Combine(_workspace, "RestoreCancellation", "second.tmp");
        await store.SaveHistoryAsync(
        [
            new CleanerCleanupBatch
            {
                BatchId = "restore-cancellation",
                Entries =
                [
                    CreateRestorableEntry("first", firstOriginal, firstBackup, 5),
                    CreateRestorableEntry("second", secondOriginal, secondBackup, 6)
                ]
            }
        ]);
        CleanerExecutionService service = CreateService(store);
        using CancellationTokenSource cts = new();
        var progress = new InlineProgress<CleanerExecutionProgress>(value =>
        {
            if (value.ProgressValue == 1) cts.Cancel();
        });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.RestoreLatestAsync(progress, cts.Token));

        CleanerCleanupBatch saved = Assert.Single(await store.LoadHistoryAsync());
        Assert.True(saved.Entries[0].Restored);
        Assert.Equal("Restored", saved.Entries[0].Status);
        Assert.False(saved.Entries[1].Restored);
        Assert.True(File.Exists(firstOriginal));
        Assert.True(File.Exists(secondBackup));
    }

    [Fact]
    public async Task PurgeQuarantineAsync_CancellationPersistsEntriesPurgedBeforeCancellation()
    {
        Directory.CreateDirectory(_workspace);
        CleanerStateStore store = new(Path.Combine(_workspace, "StatePurgeCancellation"));
        string firstBackup = Path.Combine(store.QuarantineRootPath, "purge-cancel", "first.tmp");
        string secondBackup = Path.Combine(store.QuarantineRootPath, "purge-cancel", "second.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(firstBackup)!);
        await File.WriteAllTextAsync(firstBackup, "first");
        await File.WriteAllTextAsync(secondBackup, "second");
        await store.SaveHistoryAsync(
        [
            new CleanerCleanupBatch
            {
                BatchId = "purge-cancellation",
                Entries =
                [
                    CreateRestorableEntry("first", Path.Combine(_workspace, "first.tmp"), firstBackup, 5),
                    CreateRestorableEntry("second", Path.Combine(_workspace, "second.tmp"), secondBackup, 6)
                ]
            }
        ]);
        CleanerExecutionService service = CreateService(store);
        using CancellationTokenSource cts = new();
        var progress = new InlineProgress<CleanerExecutionProgress>(value =>
        {
            if (value.ProgressValue == 1) cts.Cancel();
        });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.PurgeQuarantineAsync(progress, cts.Token));

        CleanerCleanupBatch saved = Assert.Single(await store.LoadHistoryAsync());
        Assert.Equal("Purged", saved.Entries[0].Status);
        Assert.False(saved.Entries[0].CanRestore);
        Assert.Equal("Completed", saved.Entries[1].Status);
        Assert.True(saved.Entries[1].CanRestore);
        Assert.False(File.Exists(firstBackup));
        Assert.True(File.Exists(secondBackup));
    }

    [Fact]
    public async Task RetryFailedEntriesAsync_CancellationPersistsEntriesRetriedBeforeCancellation()
    {
        Directory.CreateDirectory(_workspace);
        CleanerStateStore store = new(Path.Combine(_workspace, "StateRetryCancellation"));
        string firstOriginal = Path.Combine(_workspace, "RetryCancellation", "first.tmp");
        string secondOriginal = Path.Combine(_workspace, "RetryCancellation", "second.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(firstOriginal)!);
        await File.WriteAllTextAsync(firstOriginal, "first");
        await File.WriteAllTextAsync(secondOriginal, "second");
        await store.SaveHistoryAsync(
        [
            new CleanerCleanupBatch
            {
                BatchId = "retry-cancellation",
                Entries =
                [
                    CreateRetryEntry("first", firstOriginal, 5),
                    CreateRetryEntry("second", secondOriginal, 6)
                ]
            }
        ]);
        CleanerExecutionService service = CreateService(store);
        using CancellationTokenSource cts = new();
        var progress = new InlineProgress<CleanerExecutionProgress>(value =>
        {
            if (value.ProgressValue == 1) cts.Cancel();
        });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.RetryFailedEntriesAsync("retry-cancellation", progress, cts.Token));

        CleanerCleanupBatch saved = Assert.Single(await store.LoadHistoryAsync());
        Assert.Equal("Completed", saved.Entries[0].Status);
        Assert.Equal("Failed", saved.Entries[1].Status);
        Assert.False(File.Exists(firstOriginal));
        Assert.True(File.Exists(secondOriginal));
    }

    private static CleanerExecutionService CreateService(CleanerStateStore store) => new(
        new NativeFileService(),
        store,
        new CleanerLockService(),
        new CleanerPrivilegeService(),
        new CleanerBoundaryGuard());

    private static CleanerCleanupEntry CreateRestorableEntry(
        string id,
        string originalPath,
        string backupPath,
        long sizeBytes) => new()
    {
        EntryId = id,
        ItemName = id,
        OriginalPath = originalPath,
        BackupPath = backupPath,
        SizeBytes = sizeBytes,
        ExecutionMode = CleanerExecutionMode.Quarantine,
        RiskLevel = CleanerRiskLevel.Low,
        Status = "Completed",
        CanRestore = true
    };

    private static CleanerCleanupEntry CreateRetryEntry(string id, string originalPath, long sizeBytes) => new()
    {
        EntryId = id,
        ItemName = id,
        OriginalPath = originalPath,
        SizeBytes = sizeBytes,
        ExecutionMode = CleanerExecutionMode.Quarantine,
        RiskLevel = CleanerRiskLevel.Low,
        Status = "Failed",
        FailureReason = CleanerFailureReason.InUse,
        CanRestore = true
    };

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, true);
        }
    }
}

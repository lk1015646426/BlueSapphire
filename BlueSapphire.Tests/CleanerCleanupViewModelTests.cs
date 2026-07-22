using BlueSapphire.Models;
using BlueSapphire.Services;
using BlueSapphire.ViewModels.Cleaner;

namespace BlueSapphire.Tests;

public sealed class CleanerCleanupViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "BlueSapphireCleanupViewModelTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SelectingOlderBatch_ExposesItsRestorableEntries()
    {
        CleanerStateStore store = new(_root);
        CleanerCleanupBatch newest = new()
        {
            BatchId = "newest",
            CreatedAt = DateTimeOffset.Now,
            Entries =
            [
                new CleanerCleanupEntry
                {
                    EntryId = "permanent",
                    ItemName = "Permanent",
                    ExecutionMode = CleanerExecutionMode.Permanent,
                    Status = "Completed",
                    CanRestore = false
                }
            ]
        };
        CleanerCleanupBatch older = new()
        {
            BatchId = "older",
            CreatedAt = DateTimeOffset.Now.AddMinutes(-10),
            Entries =
            [
                new CleanerCleanupEntry
                {
                    EntryId = "recoverable",
                    ItemName = "Recoverable",
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    Status = "Completed",
                    CanRestore = true,
                    BackupPath = Path.Combine(_root, "backup.tmp")
                }
            ]
        };
        await store.SaveHistoryAsync([newest, older]);
        CleanerExecutionService execution = new(
            new NativeFileService(), store, new CleanerLockService(),
            new CleanerPrivilegeService(), new CleanerBoundaryGuard());
        var vm = new CleanerCleanupViewModel(
            execution,
            store,
            new NativeFileService(),
            new CleanerAuditService(store),
            new CleanerOperationCoordinator($"Local\\BlueSapphire.Tests.Cleaner.{Guid.NewGuid():N}"));

        await vm.ReloadHistoryAndExclusionsAsync();
        vm.SelectedCleanupBatch = vm.CleanupHistoryBatches.Single(batch => batch.BatchId == "older");

        Assert.Equal(2, vm.CleanupHistoryBatches.Count);
        Assert.True(vm.HasRestorableBatch);
        Assert.Equal("recoverable", Assert.Single(vm.LatestCleanupEntries).EntryId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

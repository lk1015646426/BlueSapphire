using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public sealed class CleanerAIToolActionProviderTests
{
    [Fact]
    public void RegisterHandlers_ExposesCleanerActions()
    {
        var provider = new CleanerAIToolActionProvider(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        var registry = new AIToolActionHandlerRegistry();

        provider.RegisterHandlers(registry);

        Assert.Equal(
            new[]
            {
                "analyze_latest_cleanup_log",
                "create_cleaner_rule_draft",
                "execute_cleanup",
                "start_smart_cleanup"
            },
            registry.SnapshotNames());
        Assert.DoesNotContain("analyze_media_folder", registry.SnapshotNames());
    }

    [Fact]
    public async Task AnalyzeLatestCleanupLog_ReturnsStructuredSummaryWithoutLocalPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerAI", Guid.NewGuid().ToString("N"));
        try
        {
            CleanerStateStore store = new(root);
            await store.SaveHistoryAsync(
            [
                new BlueSapphire.Models.CleanerCleanupBatch
                {
                    BatchId = "batch",
                    Entries =
                    [
                        new BlueSapphire.Models.CleanerCleanupEntry
                        {
                            ItemName = "Browser cache",
                            OriginalPath = @"C:\Users\Alice\SecretProject\cache.bin",
                            BackupPath = @"D:\PrivateBackup\cache.bin",
                            Status = "Completed",
                            ExecutionMode = BlueSapphire.Models.CleanerExecutionMode.Quarantine,
                            SizeBytes = 128
                        }
                    ]
                }
            ]);
            var provider = new CleanerAIToolActionProvider(
                null!, null!, null!, null!, null!, null!,
                new AIPrivacyService(), null!, null!,
                operationCoordinator: new CleanerOperationCoordinator(
                    $"Local\\BlueSapphire.Tests.Cleaner.{Guid.NewGuid():N}"),
                stateStore: store);
            var registry = new AIToolActionHandlerRegistry();
            provider.RegisterHandlers(registry);

            string? result = await registry.TryExecuteAsync(
                "analyze_latest_cleanup_log",
                string.Empty,
                new AIToolExecutionContext());

            Assert.NotNull(result);
            Assert.Contains("Browser cache", result);
            Assert.DoesNotContain("Alice", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SecretProject", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PrivateBackup", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OriginalPath", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("BackupPath", result, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

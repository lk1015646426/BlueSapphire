using BlueSapphire.Models;
using BlueSapphire.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSapphire.Tests;

public sealed class CleanerP0AcceptanceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "BlueSapphireCleanerP0Acceptance",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ControlledFilesystemFlow_ScanQuarantineRestoreAndPurgeMatchesAccounting()
    {
        string candidateRoot = Path.Combine(_root, "Candidate");
        string stateRoot = Path.Combine(_root, "State");
        string rulePath = Path.Combine(_root, "CleanerRules.json");
        Directory.CreateDirectory(candidateRoot);

        byte[] payload = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(payload);
        string candidatePath = Path.Combine(candidateRoot, "acceptance.cache");
        await File.WriteAllBytesAsync(candidatePath, payload);

        CleanerRuleManifest manifest = new()
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "p0_acceptance",
                    Name = "P0 Acceptance Cache",
                    Description = "Controlled acceptance target",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [candidateRoot],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire Tests"
                }
            ]
        };
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(rulePath, JsonSerializer.Serialize(manifest, options));

        CleanerStateStore store = new(stateRoot);
        CleanerScanService scanner = new(
            new CleanerRuleService(store, rulePath),
            new CleanerRiskEvaluator(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService());
        CleanerExecutionService executor = new(
            new NativeFileService(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService(),
            new CleanerBoundaryGuard());

        CleanerScanReport report = await scanner.ScanAsync(
            CleanerScanScope.Quick,
            new CleanerScanOptions
            {
                AnalysisDriveRoots = [Path.GetPathRoot(candidateRoot)!],
                IncludeLargeObjectAnalysis = false,
                IncludeOrphanResidueAnalysis = false
            },
            null,
            CancellationToken.None);

        CleanerScanItem scanned = Assert.Single(report.Items);
        Assert.Equal(payload.Length, scanned.SizeBytes);
        Assert.Equal(Path.GetFullPath(candidateRoot).TrimEnd('\\'), scanned.Path.TrimEnd('\\'));

        CleanerCleanupBatch firstBatch = await executor.ExecuteAsync(
            [scanned], CleanerScanScope.Quick, null, CancellationToken.None);
        CleanerCleanupEntry firstEntry = Assert.Single(firstBatch.Entries);
        Assert.Equal(0, firstBatch.ReleasedBytes);
        Assert.Equal(payload.Length, firstBatch.RecoverableBytes);
        Assert.False(File.Exists(candidatePath));
        Assert.True(File.Exists(firstEntry.BackupPath));
        Assert.StartsWith(
            Path.GetFullPath(store.QuarantineRootPath).TrimEnd('\\') + "\\",
            Path.GetFullPath(firstEntry.BackupPath),
            StringComparison.OrdinalIgnoreCase);

        CleanerRestoreSummary restore = await executor.RestoreLatestAsync(CancellationToken.None);
        Assert.Equal(1, restore.RestoredCount);
        Assert.True(File.Exists(candidatePath));

        CleanerCleanupBatch secondBatch = await executor.ExecuteAsync(
            [scanned], CleanerScanScope.Quick, null, CancellationToken.None);
        Assert.Equal(payload.Length, secondBatch.RecoverableBytes);

        CleanerQuarantinePurgeSummary purge = await executor.PurgeQuarantineAsync(CancellationToken.None);
        Assert.Equal(1, purge.PurgedCount);
        Assert.Equal(payload.Length, purge.ReleasedBytes);
        Assert.False(File.Exists(candidatePath));
        Assert.Empty(Directory.EnumerateFiles(store.QuarantineRootPath, "*", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

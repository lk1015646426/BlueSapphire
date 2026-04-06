using BlueSapphire.Models;
using BlueSapphire.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSapphire.Tests;

public class CleanerScanServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerScanTests", Guid.NewGuid().ToString("N"));
    private readonly string _builtInRulePath;

    public CleanerScanServiceTests()
    {
        Directory.CreateDirectory(_rootPath);
        _builtInRulePath = Path.Combine(_rootPath, "CleanerRules.json");
    }

    [Fact]
    public async Task DeepScan_ReusesRecentQuickScanResults()
    {
        string quickRoot = Path.Combine(_rootPath, "QuickCache");
        string deepRoot = Path.Combine(_rootPath, "DeepLogs");
        Directory.CreateDirectory(quickRoot);
        Directory.CreateDirectory(deepRoot);

        await File.WriteAllTextAsync(Path.Combine(quickRoot, "a.tmp"), new string('a', 4096));
        await File.WriteAllTextAsync(Path.Combine(deepRoot, "b.log"), new string('b', 2048));

        CleanerRuleManifest manifest = new()
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "quick_cache",
                    Name = "Quick Cache",
                    Description = "Quick cache rule",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.Directory,
                    Paths = [quickRoot],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire"
                },
                new CleanerRuleDefinition
                {
                    Id = "deep_logs",
                    Name = "Deep Logs",
                    Description = "Deep log rule",
                    Category = "app_logs",
                    Scope = CleanerScanScope.Deep,
                    ScanKind = CleanerScanKind.Directory,
                    Paths = [deepRoot],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire"
                }
            ]
        };

        JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        await File.WriteAllTextAsync(_builtInRulePath, JsonSerializer.Serialize(manifest, jsonOptions));

        CleanerStateStore store = new(_rootPath);
        CleanerRuleService ruleService = new(store, _builtInRulePath);
        CleanerLockService lockService = new();
        CleanerScanService scanService = new(
            ruleService,
            new CleanerRiskEvaluator(),
            store,
            lockService,
            new CleanerPrivilegeService());

        CleanerScanOptions options = new()
        {
            AnalysisDriveRoots = [_rootPath],
            IncludeLargeObjectAnalysis = false,
            IncludeOrphanResidueAnalysis = false
        };

        CleanerScanReport quickReport = await scanService.ScanAsync(
            CleanerScanScope.Quick,
            options,
            progress: null,
            CancellationToken.None);

        CleanerScanReport deepReport = await scanService.ScanAsync(
            CleanerScanScope.Deep,
            options,
            progress: null,
            CancellationToken.None);

        Assert.Single(quickReport.Items);
        Assert.True(deepReport.UsedIncrementalReuse);
        Assert.Equal(quickReport.Items.Count, deepReport.ReusedItemCount);
        Assert.Contains(deepReport.Items, item => item.RuleId == "quick_cache");
        Assert.Contains(deepReport.Items, item => item.RuleId == "deep_logs");
    }

    [Fact]
    public async Task ScanAsync_ExclusionUsesPathBoundary()
    {
        string ruleRoot = Path.Combine(_rootPath, "RuleRoot");
        string excludedRoot = Path.Combine(ruleRoot, "A");
        string siblingRoot = Path.Combine(ruleRoot, "AB");
        Directory.CreateDirectory(excludedRoot);
        Directory.CreateDirectory(siblingRoot);

        await File.WriteAllTextAsync(Path.Combine(excludedRoot, "skip.tmp"), new string('s', 1024));
        await File.WriteAllTextAsync(Path.Combine(siblingRoot, "keep.tmp"), new string('k', 2048));

        CleanerRuleManifest manifest = new()
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "boundary_rule",
                    Name = "Boundary Rule",
                    Description = "Boundary exclusion rule",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.Directory,
                    Paths = [ruleRoot],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire"
                }
            ]
        };

        JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        await File.WriteAllTextAsync(_builtInRulePath, JsonSerializer.Serialize(manifest, jsonOptions));

        string stateRoot = Path.Combine(_rootPath, "State");
        CleanerStateStore store = new(stateRoot);
        await store.SaveExclusionsAsync(
        [
            new CleanerExclusionEntry
            {
                Path = excludedRoot,
                CreatedAt = DateTimeOffset.Now
            }
        ]);

        CleanerScanService scanService = new(
            new CleanerRuleService(store, _builtInRulePath),
            new CleanerRiskEvaluator(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService());

        CleanerScanReport report = await scanService.ScanAsync(
            CleanerScanScope.Quick,
            new CleanerScanOptions
            {
                AnalysisDriveRoots = [_rootPath],
                IncludeLargeObjectAnalysis = false,
                IncludeOrphanResidueAnalysis = false
            },
            progress: null,
            CancellationToken.None);

        CleanerScanItem item = Assert.Single(report.Items);
        Assert.Equal(1, item.FileCount);
        Assert.Equal(2048, item.SizeBytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}

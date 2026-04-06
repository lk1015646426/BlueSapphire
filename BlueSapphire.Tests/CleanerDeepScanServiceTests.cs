using BlueSapphire.Models;
using BlueSapphire.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSapphire.Tests;

public class CleanerDeepScanServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BlueSapphireDeepScanTests", Guid.NewGuid().ToString("N"));
    private readonly string _builtInRulePath;

    public CleanerDeepScanServiceTests()
    {
        Directory.CreateDirectory(_root);
        _builtInRulePath = Path.Combine(_root, "CleanerRules.json");
    }

    [Fact]
    public async Task ScanAsync_CombinesCoreDeepScanWithLargeObjectAnalysis()
    {
        string deepRuleRoot = Path.Combine(_root, "DeepRuleRoot");
        string analysisRoot = Path.Combine(_root, "Projects");
        Directory.CreateDirectory(deepRuleRoot);
        Directory.CreateDirectory(analysisRoot);

        await File.WriteAllTextAsync(Path.Combine(deepRuleRoot, "trace.log"), new string('d', 4096));
        await File.WriteAllBytesAsync(Path.Combine(analysisRoot, "archive.bin"), new byte[8192]);

        CleanerRuleManifest manifest = new()
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "deep_rule",
                    Name = "Deep Rule",
                    Description = "Deep rule for test",
                    Category = "app_logs",
                    Scope = CleanerScanScope.Deep,
                    ScanKind = CleanerScanKind.Directory,
                    Paths = [deepRuleRoot],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire"
                }
            ]
        };

        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        await File.WriteAllTextAsync(_builtInRulePath, JsonSerializer.Serialize(manifest, options));

        CleanerStateStore store = new(_root);
        CleanerRuleService ruleService = new(store, _builtInRulePath);
        CleanerLockService lockService = new();
        CleanerRiskEvaluator riskEvaluator = new();
        CleanerScanService scanService = new(
            ruleService,
            riskEvaluator,
            store,
            lockService,
            new CleanerPrivilegeService());

        CleanerDeepScanService service = new(
            scanService,
            store,
            new CleanerSpaceAnalysisService(
                riskEvaluator,
                lockService,
                largeDirectoryThresholdBytes: 1024,
                largeFileThresholdBytes: 2048,
                maxCandidateDirectoriesPerRoot: 8,
                maxVisitedDirectories: 64,
                maxVisitedFiles: 512),
            new CleanerOrphanResidueService(lockService));

        CleanerDeepScanResult result = await service.ScanAsync(
            new CleanerScanOptions
            {
                AnalysisDriveRoots = [_root],
                IncludeLargeObjectAnalysis = true,
                IncludeOrphanResidueAnalysis = false
            },
            progress: null,
            CancellationToken.None);

        Assert.Contains(result.Report.Items, item => item.RuleId == "deep_rule");
        Assert.Contains(result.Report.Items, item => item.RuleId == "analysis_large_directory");
        Assert.Contains(result.Report.Items, item => item.RuleId == "analysis_large_file");
        Assert.True(result.SpaceAnalysis.Attempted);
        Assert.False(result.SpaceAnalysis.WasSkipped);
        Assert.True(result.SpaceAnalysis.AddedCount >= 1);
        Assert.False(result.OrphanResidue.Attempted);
    }

    [Fact]
    public async Task ScanAsync_DoesNotAddAnalysisItemsWhenDisabled()
    {
        string deepRuleRoot = Path.Combine(_root, "DeepRuleRootOnly");
        Directory.CreateDirectory(deepRuleRoot);
        await File.WriteAllTextAsync(Path.Combine(deepRuleRoot, "trace.log"), new string('d', 4096));

        CleanerRuleManifest manifest = new()
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "deep_rule_only",
                    Name = "Deep Rule Only",
                    Description = "Deep rule only",
                    Category = "app_logs",
                    Scope = CleanerScanScope.Deep,
                    ScanKind = CleanerScanKind.Directory,
                    Paths = [deepRuleRoot],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire"
                }
            ]
        };

        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        await File.WriteAllTextAsync(_builtInRulePath, JsonSerializer.Serialize(manifest, options));

        CleanerStateStore store = new(_root);
        CleanerRuleService ruleService = new(store, _builtInRulePath);
        CleanerLockService lockService = new();
        CleanerRiskEvaluator riskEvaluator = new();
        CleanerDeepScanService service = new(
            new CleanerScanService(ruleService, riskEvaluator, store, lockService, new CleanerPrivilegeService()),
            store,
            new CleanerSpaceAnalysisService(riskEvaluator, lockService, largeDirectoryThresholdBytes: 1, largeFileThresholdBytes: 1),
            new CleanerOrphanResidueService(lockService));

        CleanerDeepScanResult result = await service.ScanAsync(
            new CleanerScanOptions
            {
                AnalysisDriveRoots = [_root],
                IncludeLargeObjectAnalysis = false,
                IncludeOrphanResidueAnalysis = false
            },
            progress: null,
            CancellationToken.None);

        Assert.Single(result.Report.Items);
        Assert.Equal("deep_rule_only", result.Report.Items[0].RuleId);
        Assert.False(result.SpaceAnalysis.Attempted);
        Assert.False(result.OrphanResidue.Attempted);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

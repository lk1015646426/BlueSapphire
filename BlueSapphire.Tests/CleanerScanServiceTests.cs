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

    [Fact]
    public async Task ScanAsync_WildcardPathIncludesEveryMatchingBrowserProfile()
    {
        string userDataRoot = Path.Combine(_rootPath, "User Data");
        string defaultCache = Path.Combine(userDataRoot, "Default", "Cache", "Cache_Data");
        string profileCache = Path.Combine(userDataRoot, "Profile 1", "Cache", "Cache_Data");
        string unrelatedData = Path.Combine(userDataRoot, "System Profile", "Storage");
        Directory.CreateDirectory(defaultCache);
        Directory.CreateDirectory(profileCache);
        Directory.CreateDirectory(unrelatedData);

        await File.WriteAllTextAsync(Path.Combine(defaultCache, "default.cache"), new string('d', 1024));
        await File.WriteAllTextAsync(Path.Combine(profileCache, "profile.cache"), new string('p', 2048));
        await File.WriteAllTextAsync(Path.Combine(unrelatedData, "keep.db"), new string('k', 4096));

        CleanerRuleManifest manifest = new()
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "browser_profiles",
                    Name = "Browser Profiles",
                    Description = "Browser profile caches",
                    Category = "browser_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [Path.Combine(userDataRoot, "*", "Cache", "Cache_Data")],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true,
                    OwnerApp = "Browser"
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

        CleanerStateStore store = new(Path.Combine(_rootPath, "State"));
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

        Assert.Equal(2, report.Items.Count);
        Assert.Equal(3072, report.Items.Sum(item => item.SizeBytes));
        Assert.DoesNotContain(report.Items, item => item.Path.Contains("System Profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_AgePoliciesProduceDisjointTempBucketsAndTargetFiles()
    {
        string tempRoot = Path.Combine(_rootPath, "AgeLayeredTemp");
        Directory.CreateDirectory(tempRoot);
        string recent = Path.Combine(tempRoot, "recent.tmp");
        string review = Path.Combine(tempRoot, "review.tmp");
        string stale = Path.Combine(tempRoot, "stale.tmp");
        await File.WriteAllTextAsync(recent, "r");
        await File.WriteAllTextAsync(review, "review");
        await File.WriteAllTextAsync(stale, "stale-data");
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddHours(-6));
        File.SetLastWriteTimeUtc(review, DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-10));

        CleanerRuleManifest manifest = new()
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "temp_stale",
                    Name = "Stale temp",
                    Description = "At least seven days old",
                    Category = "system_temp",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [tempRoot],
                    MinAgeDays = 7,
                    ExecutionMode = CleanerExecutionMode.Permanent,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true
                },
                new CleanerRuleDefinition
                {
                    Id = "temp_review",
                    Name = "Recent temp",
                    Description = "Between one and seven days old",
                    Category = "system_temp",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [tempRoot],
                    MinAgeDays = 1,
                    MaxAgeDays = 7,
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Medium,
                    DefaultSelected = false
                }
            ]
        };
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        await File.WriteAllTextAsync(_builtInRulePath, JsonSerializer.Serialize(manifest, options));

        CleanerStateStore store = new(Path.Combine(_rootPath, "AgeState"));
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
            null,
            CancellationToken.None);

        CleanerScanItem staleItem = Assert.Single(report.Items, item => item.RuleId == "temp_stale");
        CleanerScanItem reviewItem = Assert.Single(report.Items, item => item.RuleId == "temp_review");
        Assert.Equal([stale], staleItem.TargetPaths);
        Assert.Equal([review], reviewItem.TargetPaths);
        Assert.Equal("stale-data".Length, staleItem.SizeBytes);
        Assert.Equal("review".Length, reviewItem.SizeBytes);
        Assert.DoesNotContain(report.Items.SelectMany(item => item.TargetPaths), path => path == recent);
    }

    [Fact]
    public async Task ScanAsync_RunningOwnerProcessBlocksSelectionBeforeCleanup()
    {
        string cacheRoot = Path.Combine(_rootPath, "RunningAppCache");
        Directory.CreateDirectory(cacheRoot);
        await File.WriteAllTextAsync(Path.Combine(cacheRoot, "cache.bin"), "cache");
        string currentProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

        CleanerRuleManifest manifest = new()
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "running_app",
                    Name = "Running app cache",
                    Description = "Must be blocked while owner runs",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [cacheRoot],
                    ProcessNames = [currentProcessName],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true,
                    OwnerApp = "Test App"
                }
            ]
        };
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        await File.WriteAllTextAsync(_builtInRulePath, JsonSerializer.Serialize(manifest, options));

        CleanerStateStore store = new(Path.Combine(_rootPath, "RunningState"));
        CleanerScanService scanService = new(
            new CleanerRuleService(store, _builtInRulePath),
            new CleanerRiskEvaluator(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService());

        CleanerScanItem item = Assert.Single((await scanService.ScanAsync(
            CleanerScanScope.Quick,
            new CleanerScanOptions
            {
                AnalysisDriveRoots = [_rootPath],
                IncludeLargeObjectAnalysis = false,
                IncludeOrphanResidueAnalysis = false
            },
            null,
            CancellationToken.None)).Items);

        Assert.True(item.IsLocked);
        Assert.False(item.CanSelect);
        Assert.Contains(currentProcessName, item.LockedByProcesses, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_OnlyReturnsRulePathsFromSelectedDrives()
    {
        string cacheRoot = Path.Combine(_rootPath, "DriveScopedCache");
        Directory.CreateDirectory(cacheRoot);
        await File.WriteAllTextAsync(Path.Combine(cacheRoot, "cache.bin"), "cache");

        CleanerRuleManifest manifest = new()
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "drive_scoped_cache",
                    Name = "Drive scoped cache",
                    Description = "Must follow selected drives",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [cacheRoot],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true
                }
            ]
        };
        JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        await File.WriteAllTextAsync(_builtInRulePath, JsonSerializer.Serialize(manifest, jsonOptions));

        CleanerStateStore store = new(Path.Combine(_rootPath, "DriveScopeState"));
        CleanerScanService scanService = new(
            new CleanerRuleService(store, _builtInRulePath),
            new CleanerRiskEvaluator(),
            store,
            new CleanerLockService(),
            new CleanerPrivilegeService());

        CleanerScanReport excludedReport = await scanService.ScanAsync(
            CleanerScanScope.Quick,
            new CleanerScanOptions { AnalysisDriveRoots = [@"Z:\"] },
            null,
            CancellationToken.None);
        CleanerScanReport includedReport = await scanService.ScanAsync(
            CleanerScanScope.Quick,
            new CleanerScanOptions { AnalysisDriveRoots = [Path.GetPathRoot(_rootPath)!] },
            null,
            CancellationToken.None);

        Assert.Empty(excludedReport.Items);
        Assert.Equal(@"Z:\", Assert.Single(excludedReport.AnalysisDriveRoots));
        Assert.Single(includedReport.Items);
    }

    [Fact]
    public async Task DirectoryContentsExecution_UsesTheConfirmedScanSnapshot()
    {
        string cacheRoot = Path.Combine(_rootPath, "SnapshotCache");
        Directory.CreateDirectory(cacheRoot);
        string scannedFile = Path.Combine(cacheRoot, "scanned.tmp");
        string lateFile = Path.Combine(cacheRoot, "created-after-scan.tmp");
        await File.WriteAllTextAsync(scannedFile, "scanned");
        CleanerRuleManifest manifest = new()
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "snapshot_cache",
                    Name = "Snapshot cache",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [cacheRoot],
                    BoundaryRoots = [cacheRoot],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true
                }
            ]
        };
        JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        await File.WriteAllTextAsync(_builtInRulePath, JsonSerializer.Serialize(manifest, jsonOptions));
        CleanerStateStore store = new(Path.Combine(_rootPath, "SnapshotState"));
        CleanerLockService lockService = new();
        CleanerPrivilegeService privilegeService = new();
        CleanerScanService scanner = new(
            new CleanerRuleService(store, _builtInRulePath),
            new CleanerRiskEvaluator(),
            store,
            lockService,
            privilegeService);

        CleanerScanItem item = Assert.Single((await scanner.ScanAsync(
            CleanerScanScope.Quick,
            new CleanerScanOptions { AnalysisDriveRoots = [Path.GetPathRoot(_rootPath)!] },
            null,
            CancellationToken.None)).Items);
        await File.WriteAllTextAsync(lateFile, "late");
        CleanerExecutionService executor = new(
            new NativeFileService(), store, lockService, privilegeService, new CleanerBoundaryGuard());

        await executor.ExecuteAsync([item], CleanerScanScope.Quick, null, CancellationToken.None);

        Assert.False(File.Exists(scannedFile));
        Assert.True(File.Exists(lateFile));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}

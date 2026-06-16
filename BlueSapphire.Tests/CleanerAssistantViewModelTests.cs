using Microsoft.Extensions.Logging.Abstractions;
using BlueSapphire.Models;
using BlueSapphire.Services;
using BlueSapphire.ViewModels;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSapphire.Tests;

public class CleanerAssistantViewModelTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerVmTests", Guid.NewGuid().ToString("N"));
    private readonly string _rulePath;

    public CleanerAssistantViewModelTests()
    {
        Directory.CreateDirectory(_rootPath);
        _rulePath = Path.Combine(_rootPath, "CleanerRules.json");
    }

    // ================================================================
    // Test 1: 构造函数正常创建所有子 ViewModel
    // ================================================================
    [Fact]
    public void Constructor_CreatesAllSubViewModels()
    {
        CleanerAssistantViewModel vm = CreateViewModel();

        Assert.NotNull(vm.Scan);
        Assert.NotNull(vm.Cleanup);
        Assert.NotNull(vm.Automation);
        Assert.NotNull(vm.Rule);
        Assert.NotNull(vm.Drive);
        Assert.NotNull(vm.Settings);
    }

    // ================================================================
    // Test 2: 提权状态正确反映当前权限
    // ================================================================
    [Fact]
    public void IsElevatedMode_ReflectsActualPrivilegeState()
    {
        CleanerAssistantViewModel vm = CreateViewModel();

        Assert.False(vm.IsElevatedMode);
        Assert.True(vm.CanEnterElevatedMode);
        Assert.Equal("标准模式", vm.PrivilegeModeText);
    }

    // ================================================================
    // Test 3: 扫描前状态正确（未忙碌、无结果）
    // ================================================================
    [Fact]
    public void InitialState_HasNoScanResults()
    {
        CleanerAssistantViewModel vm = CreateViewModel();

        Assert.False(vm.Scan.IsBusy);
        Assert.False(vm.Scan.HasSafeItems);
        Assert.False(vm.Scan.HasReviewItems);
        Assert.False(vm.Scan.HasViewOnlyItems);
        Assert.False(vm.Scan.CanRunCleanup);
    }

    // ================================================================
    // Test 4: 快速扫描生成正确的结果分桶
    // ================================================================
    [Fact]
    public async Task QuickScan_BucketsItemsCorrectly()
    {
        string cacheDir = Path.Combine(_rootPath, "TestCache");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "cache.tmp"), new string('x', 2048));

        await WriteRuleAsync(new CleanerRuleManifest
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "test_cache",
                    Name = "Test Cache",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.Directory,
                    Paths = [cacheDir],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire"
                }
            ]
        });

        CleanerAssistantViewModel vm = CreateViewModel();

        // 触发快速扫描
        await vm.Scan.StartQuickScanCommand.ExecuteAsync(null);

        Assert.True(vm.Scan.HasSafeItems);
        Assert.Single(vm.Scan.SafeItems);
        Assert.Equal("Test Cache", vm.Scan.SafeItems[0].Name);
    }

    // ================================================================
    // Test 5: 扫描进度正确更新
    // ================================================================
    [Fact]
    public async Task Scan_UpdatesProgressAndStatus()
    {
        string cacheDir = Path.Combine(_rootPath, "ProgressTest");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "f1.tmp"), new string('a', 1024));

        await WriteRuleAsync(new CleanerRuleManifest
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "prog_cache",
                    Name = "Progress Cache",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.Directory,
                    Paths = [cacheDir],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire"
                }
            ]
        });

        CleanerAssistantViewModel vm = CreateViewModel();

        await vm.Scan.StartQuickScanCommand.ExecuteAsync(null);

        Assert.False(vm.Scan.IsBusy);
        Assert.Contains("扫描完成", vm.Scan.StatusMainText);
        Assert.True(vm.Scan.ProgressValue >= 0);
    }

    // ================================================================
    // Test 6: 取消扫描操作不抛异常
    // ================================================================
    [Fact]
    public void CancelCurrentOperation_SetsCancellationWithoutException()
    {
        CleanerAssistantViewModel vm = CreateViewModel();

        // 未开始扫描时取消应该不抛异常
        var ex = Record.Exception(() => vm.Scan.CancelCurrentOperationCommand.Execute(null));
        Assert.Null(ex);
    }

    // ================================================================
    // Test 7: 清理命令在无选中项时不应执行
    // ================================================================
    [Fact]
    public void CanRunCleanup_ReturnsFalseWhenNoItemsSelected()
    {
        CleanerAssistantViewModel vm = CreateViewModel();

        Assert.False(vm.Scan.CanRunCleanup);
    }

    // ================================================================
    // Test 8: 磁盘选项初始化成功
    // ================================================================
    [Fact]
    public async Task DriveOptions_InitializesWithAvailableDrives()
    {
        CleanerAssistantViewModel vm = CreateViewModel();

        await vm.Drive.InitializeAsync();

        Assert.True(vm.Drive.DriveOptions.Count > 0);
        Assert.Contains(vm.Drive.DriveOptions, d => d.IsSelected);
    }

    // ================================================================
    // Test 9: 排除项添加后扫描结果过滤正确
    // ================================================================
    [Fact]
    public async Task Exclusion_FiltersScanResults()
    {
        string baseDir = Path.Combine(_rootPath, "ExclBase");
        string keepDir = Path.Combine(baseDir, "Keep");
        string skipDir = Path.Combine(baseDir, "Skip");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(skipDir);
        await File.WriteAllTextAsync(Path.Combine(keepDir, "keep.tmp"), new string('k', 1024));
        await File.WriteAllTextAsync(Path.Combine(skipDir, "skip.tmp"), new string('s', 1024));

        await WriteRuleAsync(new CleanerRuleManifest
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "excl_rule",
                    Name = "Exclusion Rule",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.Directory,
                    Paths = [baseDir],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire"
                }
            ]
        });

        CleanerAssistantViewModel vm = CreateViewModel();

        // 先添加排除项
        await vm.Cleanup.AddToExclusionsCommand.ExecuteAsync(
            new CleanerScanItem { Path = skipDir, Name = "Skip", RuleId = "excl_rule" });

        // 再扫描
        await vm.Scan.StartQuickScanCommand.ExecuteAsync(null);

        // Keep 目录应该被扫描到，Skip 目录应该被排除
        Assert.All(vm.Scan.SafeItems, item => Assert.DoesNotContain("Skip", item.Path));
    }

    // ================================================================
    // Test 10: 消息注册不抛异常
    // ================================================================
    [Fact]
    public void MessageRegistration_DoesNotThrow()
    {
        var ex = Record.Exception(() => CreateViewModel());
        Assert.Null(ex);
    }

    // ================================================================
    // Helper Methods
    // ================================================================
    private CleanerAssistantViewModel CreateViewModel()
    {
        Directory.CreateDirectory(_rootPath);

        CleanerStateStore stateStore = new(_rootPath);
        CleanerProfileService profileService = new(stateStore);
        CleanerRuleService ruleService = new(stateStore, _rulePath);
        CleanerLockService lockService = new();
        CleanerPrivilegeService privilegeService = new();
        CleanerAuditService auditService = new(stateStore);
        CleanerAutomationScheduleService scheduleService = new(
            executablePathProvider: () => @"C:\Apps\BlueSapphire.exe",
            commandRunner: _ => Task.FromResult(new CleanerTaskCommandResult(0, string.Empty, string.Empty)));
        CleanerAutomationService automationService = new(stateStore, scheduleService);
        CleanerLaunchActionService launchActionService = new(() => string.Empty);
        CleanerDriveService driveService = new();
        CleanerRiskEvaluator riskEvaluator = new();
        CleanerSpaceAnalysisService spaceAnalysisService = new(riskEvaluator, lockService, (AIClassifierService?)null);
        CleanerOrphanResidueService orphanResidueService = new(lockService);

        CleanerScanService scanService = new(ruleService, riskEvaluator, stateStore, lockService, privilegeService);
        CleanerDeepScanService deepScanService = new(scanService, stateStore, spaceAnalysisService, orphanResidueService, NullLogger<CleanerDeepScanService>.Instance);
        CleanerTelemetryService telemetryService = new(stateStore, auditService, ruleService, profileService);
        CleanerRecommendationService recommendationService = new();
        CleanerExecutionService executionService = new(
            new NativeFileService(), stateStore, lockService, privilegeService, new CleanerBoundaryGuard());

        return new CleanerAssistantViewModel(
            scanService, executionService, stateStore, ruleService,
            new NativeFileService(), privilegeService, auditService,
            automationService, launchActionService, driveService,
            deepScanService, profileService, telemetryService,
            recommendationService,
            new CleanerSettingsViewModel(automationService, telemetryService));
    }

    private async Task WriteRuleAsync(CleanerRuleManifest manifest)
    {
        JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        await File.WriteAllTextAsync(_rulePath, JsonSerializer.Serialize(manifest, jsonOptions));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}

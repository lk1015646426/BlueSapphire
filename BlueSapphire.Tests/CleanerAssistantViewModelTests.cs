using Microsoft.Extensions.Logging.Abstractions;
using BlueSapphire.Models;
using BlueSapphire.Interfaces;
using BlueSapphire.Services;
using BlueSapphire.ViewModels;
using System.Diagnostics;
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

    [Fact]
    public async Task QuickScan_CancelCommandStopsActiveScanWithoutBlockingCaller()
    {
        string cacheDir = Path.Combine(_rootPath, "CancelableScan");
        Directory.CreateDirectory(cacheDir);
        for (int i = 0; i < 2_000; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(cacheDir, $"cache-{i:D4}.tmp"), "x");
        }

        await WriteRuleAsync(new CleanerRuleManifest
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "cancelable_cache",
                    Name = "Cancelable Cache",
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
        Stopwatch startLatency = Stopwatch.StartNew();
        Task scanTask = vm.Scan.StartQuickScanCommand.ExecuteAsync(null);
        startLatency.Stop();

        Assert.True(startLatency.Elapsed < TimeSpan.FromSeconds(1), "启动扫描不应阻塞调用线程。");
        Assert.True(vm.Scan.IsBusy);

        vm.Scan.CancelCurrentOperationCommand.Execute(null);
        await scanTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(vm.Scan.IsBusy);
        Assert.Equal(CleanerScanState.Idle, vm.Scan.CurrentScanState);
        Assert.Equal("已取消", vm.Scan.StatusMainText);
    }

    [Fact]
    public async Task Rescan_HidesStaleResultsAndCancellationRestoresLastCompleteReport()
    {
        string cacheDir = Path.Combine(_rootPath, "RescanState");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "initial.tmp"), "initial");

        await WriteRuleAsync(new CleanerRuleManifest
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "rescan_state",
                    Name = "Rescan state",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [cacheDir],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true
                }
            ]
        });

        CleanerAssistantViewModel vm = CreateViewModel();
        await vm.Scan.StartQuickScanCommand.ExecuteAsync(null);
        Assert.True(vm.Scan.HasSafeItems);

        for (int i = 0; i < 2_000; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(cacheDir, $"rescan-{i:D4}.tmp"), "x");
        }

        Task rescanTask = vm.Scan.StartQuickScanCommand.ExecuteAsync(null);
        Assert.True(vm.Scan.IsScanning);
        Assert.False(vm.Scan.HasSafeItems);
        Assert.False(vm.Scan.HasAnyCategoryItems);

        vm.Scan.CancelCurrentOperationCommand.Execute(null);
        await rescanTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(CleanerScanState.Completed, vm.Scan.CurrentScanState);
        Assert.True(vm.Scan.HasSafeItems);
        Assert.Contains("上一次完整扫描结果", vm.Scan.StatusDetailText);
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

    [Fact]
    public async Task RunCleanup_ShowsInlineProgressOutcomeAndCanBeDismissed()
    {
        string cacheDir = Path.Combine(_rootPath, "CleanupExperience");
        Directory.CreateDirectory(cacheDir);
        string cacheFile = Path.Combine(cacheDir, "cache.tmp");
        await File.WriteAllTextAsync(cacheFile, new string('x', 4096));

        await WriteRuleAsync(new CleanerRuleManifest
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "cleanup_experience",
                    Name = "Cleanup Experience",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [cacheDir],
                    BoundaryRoots = [cacheDir],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire"
                }
            ]
        });

        CleanerAssistantViewModel vm = CreateViewModel();
        await vm.InitializeAsync(new ConfirmingCleanerView());
        await vm.Scan.StartQuickScanCommand.ExecuteAsync(null);

        Assert.True(vm.Scan.CanRunCleanup);
        await vm.RunCleanupCommand.ExecuteAsync(null);

        Assert.False(vm.IsCleanupRunning);
        Assert.True(vm.IsCleanupOutcomeVisible);
        Assert.True(vm.IsCleanupExperienceVisible);
        Assert.Equal("清理完成", vm.CleanupOutcomeTitle);
        Assert.Equal("4 KB", vm.CleanupRecoverableText);
        Assert.False(File.Exists(cacheFile));

        vm.DismissCleanupOutcomeCommand.Execute(null);
        Assert.False(vm.IsCleanupExperienceVisible);
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

    [Fact]
    public void SharedOperationBusyState_DisablesScanAndCleanupEntryPoints()
    {
        CleanerAssistantViewModel vm = CreateViewModel();

        vm.Scan.SetBusyState(true, "正在恢复", "正在刷新隔离区记录");

        Assert.True(vm.Scan.IsBusy);
        Assert.False(vm.Scan.IsNotScanning);
        Assert.False(vm.Scan.CanRunCleanup);
        Assert.False(vm.Scan.CanRunAutomaticLowRiskCleanupNow);
        Assert.False(vm.Cleanup.CanStartHistoryOperation);
    }

    [Fact]
    public async Task ScanEntryPoint_RefusesToStartWhileSharedCoordinatorIsBusy()
    {
        var coordinator = new CleanerOperationCoordinator(
            $"Local\\BlueSapphire.Tests.Cleaner.{Guid.NewGuid():N}");
        Assert.True(coordinator.TryAcquire(CleanerOperationKind.Cleanup, out CleanerOperationLease? lease));
        CleanerAssistantViewModel vm = CreateViewModel(coordinator);

        await vm.Scan.StartQuickScanCommand.ExecuteAsync(null);

        Assert.Equal(CleanerScanState.Idle, vm.Scan.CurrentScanState);
        Assert.Contains("另一项", vm.Scan.StatusDetailText);
        lease!.Dispose();
    }

    [Fact]
    public async Task StartingAnotherScan_ClearsPreviouslySelectedDetail()
    {
        string cacheDir = Path.Combine(_rootPath, "SelectedDetail");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "detail.tmp"), "detail");
        await WriteRuleAsync(CreateSingleDirectoryRule("selected_detail", cacheDir));

        CleanerAssistantViewModel vm = CreateViewModel();
        await vm.Scan.StartQuickScanCommand.ExecuteAsync(null);
        vm.SelectedScanItem = Assert.Single(vm.Scan.SafeItems);

        await vm.Scan.StartQuickScanCommand.ExecuteAsync(null);

        Assert.Null(vm.SelectedScanItem);
        Assert.False(vm.HasSelectedScanItem);
    }

    [Fact]
    public async Task AddingExclusion_ImmediatelyUnselectsAndHidesCurrentItemDetail()
    {
        string cacheDir = Path.Combine(_rootPath, "ImmediateExclusion");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "excluded.tmp"), "exclude");
        await WriteRuleAsync(CreateSingleDirectoryRule("immediate_exclusion", cacheDir));

        CleanerAssistantViewModel vm = CreateViewModel();
        await vm.Scan.StartQuickScanCommand.ExecuteAsync(null);
        CleanerScanItem item = Assert.Single(vm.Scan.SafeItems);
        vm.SelectedScanItem = item;

        await vm.Cleanup.AddToExclusionsCommand.ExecuteAsync(item);

        Assert.True(item.IsExcluded);
        Assert.False(item.IsSelected);
        Assert.Null(vm.SelectedScanItem);
        Assert.Equal(0, vm.Scan.TotalSelectedItemCount);
        await vm.WaitForPendingBackgroundWorkAsync();
        vm.Shutdown();
    }

    [Fact]
    public async Task AutomaticCleanupWithoutSafeItems_MarksScheduleHandled()
    {
        await WriteRuleAsync(new CleanerRuleManifest { Rules = [] });
        CleanerAssistantViewModel vm = CreateViewModel();
        await vm.InitializeAsync(new ConfirmingCleanerView());

        await vm.ExecuteAutomaticLowRiskCleanupAsync("测试自动保洁", showCompletionTip: false);

        CleanerPreferenceState preferences = await new CleanerStateStore(_rootPath).LoadPreferencesAsync();
        Assert.NotNull(preferences.LastAutoCleanupAt);
        Assert.False(vm.Scan.IsBusy);
    }

    [Fact]
    public async Task AutomaticCleanup_RefreshesResultsAfterRemovingSafeItems()
    {
        string cacheDir = Path.Combine(_rootPath, "AutomaticCleanupRefresh");
        Directory.CreateDirectory(cacheDir);
        string cacheFile = Path.Combine(cacheDir, "automatic.tmp");
        await File.WriteAllTextAsync(cacheFile, new string('a', 2048));
        await WriteRuleAsync(CreateSingleDirectoryRule("automatic_cleanup_refresh", cacheDir));

        CleanerAssistantViewModel vm = CreateViewModel();
        await vm.InitializeAsync(new ConfirmingCleanerView());

        await vm.ExecuteAutomaticLowRiskCleanupAsync("测试自动保洁", showCompletionTip: false);

        Assert.False(File.Exists(cacheFile));
        Assert.Empty(vm.Scan.AllItems);
        Assert.Equal(CleanerScanState.Completed, vm.Scan.CurrentScanState);
        Assert.True(vm.IsCleanupOutcomeVisible);
        Assert.False(vm.Scan.IsBusy);
    }

    [Fact]
    public async Task AutomaticCleanup_LeavesLowRiskPermanentItemsForManualReview()
    {
        string recoverableDir = Path.Combine(_rootPath, "AutomaticRecoverable");
        string permanentDir = Path.Combine(_rootPath, "AutomaticPermanent");
        Directory.CreateDirectory(recoverableDir);
        Directory.CreateDirectory(permanentDir);
        string recoverableFile = Path.Combine(recoverableDir, "recoverable.tmp");
        string permanentFile = Path.Combine(permanentDir, "permanent.tmp");
        await File.WriteAllTextAsync(recoverableFile, "recoverable");
        await File.WriteAllTextAsync(permanentFile, "permanent");

        await WriteRuleAsync(new CleanerRuleManifest
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = "automatic_recoverable",
                    Name = "Automatic recoverable",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [recoverableDir],
                    BoundaryRoots = [recoverableDir],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true
                },
                new CleanerRuleDefinition
                {
                    Id = "automatic_permanent",
                    Name = "Automatic permanent",
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [permanentDir],
                    BoundaryRoots = [permanentDir],
                    ExecutionMode = CleanerExecutionMode.Permanent,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true
                }
            ]
        });

        CleanerAssistantViewModel vm = CreateViewModel();
        await vm.InitializeAsync(new ConfirmingCleanerView());

        await vm.ExecuteAutomaticLowRiskCleanupAsync("测试自动保洁", showCompletionTip: false);

        Assert.False(File.Exists(recoverableFile));
        Assert.True(File.Exists(permanentFile));
        CleanerCleanupBatch latestBatch = Assert.Single(await new CleanerStateStore(_rootPath).LoadHistoryAsync());
        Assert.All(latestBatch.Entries, entry => Assert.Equal(CleanerExecutionMode.Quarantine, entry.ExecutionMode));
    }

    // ================================================================
    // Helper Methods
    // ================================================================
    private CleanerAssistantViewModel CreateViewModel(CleanerOperationCoordinator? operationCoordinator = null)
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
            new CleanerSettingsViewModel(automationService, telemetryService),
            operationCoordinator: operationCoordinator ?? new CleanerOperationCoordinator(
                $"Local\\BlueSapphire.Tests.Cleaner.{Guid.NewGuid():N}"));
    }

    private static CleanerRuleManifest CreateSingleDirectoryRule(string ruleId, string directoryPath)
    {
        return new CleanerRuleManifest
        {
            Rules =
            [
                new CleanerRuleDefinition
                {
                    Id = ruleId,
                    Name = ruleId,
                    Category = "app_cache",
                    Scope = CleanerScanScope.Quick,
                    ScanKind = CleanerScanKind.DirectoryContents,
                    Paths = [directoryPath],
                    BoundaryRoots = [directoryPath],
                    ExecutionMode = CleanerExecutionMode.Quarantine,
                    RiskLevel = CleanerRiskLevel.Low,
                    DefaultSelected = true,
                    OwnerApp = "BlueSapphire"
                }
            ]
        };
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

    private sealed class ConfirmingCleanerView : ICleanerAssistantViewInteraction
    {
        public Task ShowTipAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ShowCleanupConfirmationAsync(CleanerCleanupPlanSummary plan) => Task.FromResult(true);
        public Task<bool> ShowScanReminderConfirmationAsync() => Task.FromResult(false);
        public Task<bool> ShowRestoreConfirmationAsync(string summaryText) => Task.FromResult(false);
        public Task<bool> ShowPurgeQuarantineConfirmationAsync(int itemCount, long sizeBytes) => Task.FromResult(false);
        public Task<bool> ShowRuleDisableConfirmationAsync(string ruleName, string ruleId) => Task.FromResult(false);
        public Task<string?> PickRulePackFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> PromptRulePackUrlAsync(string? currentUrl) => Task.FromResult<string?>(null);
        public Task<string?> PromptTelemetryEndpointAsync(string? currentUrl) => Task.FromResult<string?>(null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}

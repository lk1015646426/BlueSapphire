using BlueSapphire.Services;
using BlueSapphire.ViewModels;
using System.Reflection;

namespace BlueSapphire.Tests;

public class CleanerAssistantViewModelTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerVmTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateOperationCts_DoesNotThrowWhenPreviousSourceWasAlreadyDisposed()
    {
        CleanerAssistantViewModel viewModel = CreateViewModel();
        MethodInfo createMethod = typeof(CleanerAssistantViewModel).GetMethod(
            "CreateOperationCts",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo releaseMethod = typeof(CleanerAssistantViewModel).GetMethod(
            "ReleaseOperationCts",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        CancellationTokenSource first = (CancellationTokenSource)createMethod.Invoke(viewModel, null)!;
        first.Dispose();

        CancellationTokenSource second = (CancellationTokenSource)createMethod.Invoke(viewModel, null)!;

        releaseMethod.Invoke(viewModel, [second]);
    }

    [Fact]
    public void ReplaceScanItems_CapsViewOnlyDisplayButKeepsOverallCounts()
    {
        CleanerAssistantViewModel viewModel = CreateViewModel();
        MethodInfo replaceMethod = typeof(CleanerAssistantViewModel).GetMethod(
            "ReplaceScanItems",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        List<BlueSapphire.Models.CleanerScanItem> items = Enumerable.Range(1, 18)
            .Select(index => new BlueSapphire.Models.CleanerScanItem
            {
                RuleId = $"view_only_{index}",
                Name = $"ViewOnly {index}",
                Description = "Synthetic deep-scan item",
                Category = "unknown_large",
                Path = Path.Combine(_rootPath, $"ViewOnly{index}"),
                SizeBytes = index * 1024L,
                RiskLevel = BlueSapphire.Models.CleanerRiskLevel.High,
                ExecutionMode = BlueSapphire.Models.CleanerExecutionMode.None,
                ViewOnly = true
            })
            .ToList();

        replaceMethod.Invoke(viewModel, [items]);

        Assert.Equal(12, viewModel.ViewOnlyItems.Count);
        Assert.Equal("18 项", viewModel.ViewOnlyCountText);
        Assert.True(viewModel.HasHiddenViewOnlyItems);
        Assert.Equal(6, viewModel.HiddenViewOnlyCount);
    }

    private CleanerAssistantViewModel CreateViewModel()
    {
        Directory.CreateDirectory(_rootPath);

        CleanerStateStore stateStore = new(_rootPath);
        CleanerProfileService profileService = new(stateStore);
        CleanerRuleService ruleService = new(stateStore, profileService);
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
        CleanerSpaceAnalysisService spaceAnalysisService = new(riskEvaluator, lockService);
        CleanerOrphanResidueService orphanResidueService = new(lockService);
        CleanerDeepScanService deepScanService = new(scanService: new CleanerScanService(
            ruleService,
            riskEvaluator,
            stateStore,
            lockService,
            privilegeService),
            stateStore,
            spaceAnalysisService,
            orphanResidueService);
        CleanerTelemetryService telemetryService = new(stateStore, auditService, ruleService, profileService);
        CleanerRecommendationService recommendationService = new();

        CleanerScanService scanService = new(
            ruleService,
            riskEvaluator,
            stateStore,
            lockService,
            privilegeService);

        CleanerExecutionService executionService = new(
            new NativeFileService(),
            stateStore,
            lockService,
            privilegeService,
            new CleanerBoundaryGuard());

        return new CleanerAssistantViewModel(
            scanService,
            executionService,
            stateStore,
            ruleService,
            new NativeFileService(),
            privilegeService,
            auditService,
            automationService,
            launchActionService,
            driveService,
            deepScanService,
            profileService,
            telemetryService,
            recommendationService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }
}

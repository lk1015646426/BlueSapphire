using BlueSapphire.Models;

namespace BlueSapphire.Tests;

public class CleanerModelBehaviorTests
{
    [Fact]
    public void CanRetryEntry_ReturnsFalseForBoundaryBlockedFailure()
    {
        CleanerCleanupEntry entry = new()
        {
            Status = "Failed",
            FailureReason = CleanerFailureReason.BoundaryBlocked
        };

        Assert.False(entry.CanRetryEntry);
        Assert.Contains("白名单边界", entry.RecoveryHint);
    }

    [Fact]
    public void CanRetryEntry_ReturnsTrueAndBuildsHintForLockedFailure()
    {
        CleanerCleanupEntry entry = new()
        {
            Status = "Failed",
            FailureReason = CleanerFailureReason.InUse,
            LockedByProcesses = ["Code.exe", "explorer.exe"]
        };

        Assert.True(entry.CanRetryEntry);
        Assert.Contains("Code.exe", entry.RecoveryHint);
    }

    [Fact]
    public void RecoveryHint_ExplainsElevationRequirement()
    {
        CleanerCleanupEntry entry = new()
        {
            Status = "Failed",
            FailureReason = CleanerFailureReason.AccessDenied,
            RequiresElevation = true
        };

        Assert.True(entry.CanRetryEntry);
        Assert.Contains("管理员模式", entry.RecoveryHint);
    }

    [Fact]
    public void CleanupPlan_SeparatesImmediateReleaseFromRecoverableStaging()
    {
        CleanerScanItem permanent = new()
        {
            SizeBytes = 100,
            RiskLevel = CleanerRiskLevel.Low,
            ExecutionMode = CleanerExecutionMode.Permanent,
            CanSelect = true
        };
        CleanerScanItem quarantine = new()
        {
            SizeBytes = 250,
            RiskLevel = CleanerRiskLevel.Medium,
            ExecutionMode = CleanerExecutionMode.Quarantine,
            CanSelect = true
        };

        CleanerCleanupPlanSummary plan = CleanerCleanupPlanSummary.FromItems([permanent, quarantine]);

        Assert.Equal(2, plan.ItemCount);
        Assert.Equal(100, plan.PermanentBytes);
        Assert.Equal(250, plan.QuarantineBytes);
        Assert.Equal(1, plan.ReviewItemCount);
        Assert.Contains("立即释放，不可恢复", plan.ConfirmationText);
        Assert.Contains("清空隔离区前不会释放空间", plan.ConfirmationText);
    }

    [Fact]
    public void CleanupPlan_SeparatesWindowsSystemActions()
    {
        CleanerScanItem systemItem = new()
        {
            SizeBytes = 4096,
            ExecutionMode = CleanerExecutionMode.System,
            SystemAction = CleanerSystemActionKind.DeliveryOptimization,
            RiskLevel = CleanerRiskLevel.Medium,
            CanSelect = true,
            IsSelected = true
        };

        CleanerCleanupPlanSummary plan = CleanerCleanupPlanSummary.FromItems([systemItem]);

        Assert.Equal(1, plan.SystemItemCount);
        Assert.Equal(4096, plan.SystemBytes);
        Assert.True(plan.HasIrreversibleItems);
        Assert.Contains("Windows 专用清理", plan.ConfirmationText);
    }
}

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
}

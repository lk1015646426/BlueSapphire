using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class AIOperationPolicyServiceTests
{
    [Fact]
    public void ValidateDriveRoots_RejectsSubdirectoryMasqueradingAsDrive()
    {
        var service = new AIOperationPolicyService();
        string subdirectory = Path.Combine(
            Path.GetPathRoot(Environment.SystemDirectory)!,
            "Windows");

        Assert.Throws<InvalidOperationException>(() =>
            service.ValidateDriveRoots([subdirectory]));
    }

    [Fact]
    public async Task ConfirmAsync_RequiresCallbackApprovalAndIsActionScoped()
    {
        var service = new AIOperationPolicyService();

        Assert.False(await service.ConfirmAsync(null, "delete", "abc", "确认"));
        Assert.False(await service.ConfirmAsync(_ => Task.FromResult(false), "delete", "abc", "确认"));
        Assert.True(await service.ConfirmAsync(_ => Task.FromResult(true), "delete", "abc", "确认"));
    }
}

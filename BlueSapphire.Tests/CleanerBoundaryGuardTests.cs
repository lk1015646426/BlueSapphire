using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerBoundaryGuardTests
{
    private readonly CleanerBoundaryGuard _guard = new();

    [Fact]
    public void Validate_BlocksSystemItemWhenNotElevated()
    {
        CleanerScanItem item = new()
        {
            Path = @"C:\Windows\Temp",
            RequiresElevation = true,
            BoundaryRoots = [@"C:\Windows\Temp"]
        };

        CleanerBoundaryValidationResult result = _guard.Validate(item, @"C:\Windows\Temp\a.tmp", isElevated: false);

        Assert.False(result.IsAllowed);
        Assert.Equal(CleanerFailureReason.ElevationRequired, result.FailureReason);
    }

    [Fact]
    public void Validate_BlocksTargetOutsideBoundary()
    {
        CleanerScanItem item = new()
        {
            Path = @"C:\Windows\Temp",
            RequiresElevation = true,
            BoundaryRoots = [@"C:\Windows\Temp"]
        };

        CleanerBoundaryValidationResult result = _guard.Validate(item, @"C:\Windows\System32\drivers\etc\hosts", isElevated: true);

        Assert.False(result.IsAllowed);
        Assert.Equal(CleanerFailureReason.BoundaryBlocked, result.FailureReason);
    }

    [Fact]
    public void Validate_AllowsTargetWithinBoundary()
    {
        CleanerScanItem item = new()
        {
            Path = @"C:\Windows\Temp",
            RequiresElevation = true,
            BoundaryRoots = [@"C:\Windows\Temp"]
        };

        CleanerBoundaryValidationResult result = _guard.Validate(item, @"C:\Windows\Temp\sub\a.tmp", isElevated: true);

        Assert.True(result.IsAllowed);
        Assert.Equal(CleanerFailureReason.None, result.FailureReason);
    }

    [Fact]
    public void Validate_BlocksBroadProtectedBoundaryRoot()
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        CleanerScanItem item = new()
        {
            Path = Path.Combine(programData, "Microsoft"),
            RequiresElevation = true,
            BoundaryRoots = [programData]
        };

        CleanerBoundaryValidationResult result = _guard.Validate(item, Path.Combine(programData, "test.bin"), isElevated: true);

        Assert.False(result.IsAllowed);
        Assert.Equal(CleanerFailureReason.BoundaryBlocked, result.FailureReason);
    }
}

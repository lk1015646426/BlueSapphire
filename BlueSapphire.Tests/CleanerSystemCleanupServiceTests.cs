using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public sealed class CleanerSystemCleanupServiceTests
{
    [Fact]
    public void DeliveryOptimizationCommand_IsFixedNonInteractiveAndHidden()
    {
        CleanerSystemCleanupService service = new();

        System.Diagnostics.ProcessStartInfo startInfo = service.BuildDeliveryOptimizationStartInfo();

        Assert.Equal("powershell.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Contains("-NonInteractive", startInfo.ArgumentList);
        Assert.Contains(
            startInfo.ArgumentList,
            argument => argument.Contains("Delete-DeliveryOptimizationCache -Force", StringComparison.Ordinal));
        Assert.DoesNotContain(startInfo.ArgumentList, argument => argument.Contains('$'));
    }
}

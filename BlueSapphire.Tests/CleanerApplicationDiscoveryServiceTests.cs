using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public sealed class CleanerApplicationDiscoveryServiceTests
{
    [Fact]
    public void FormatContext_IncludesVersionAndVerifiedInstallLocation()
    {
        string context = CleanerApplicationDiscoveryService.FormatContext(
            "Example App",
            "2.5.1",
            @"D:\Apps\Example");

        Assert.Equal("Example App · 版本 2.5.1 · 安装于 D:\\Apps\\Example", context);
    }
}

using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class MediaScanServiceTests
{
    [Fact]
    public void HammingDistance_ReturnsBitDifferenceCount()
    {
        ulong left = 0b101101;
        ulong right = 0b111001;

        Assert.Equal(2, MediaScanService.HammingDistance(left, right));
    }
}

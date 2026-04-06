using BlueSapphire.Services;
using Windows.Storage;

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

    [Fact]
    public async Task ComputeQuickHeaderFooterHashAsync_ReturnsHashForRealAudioSample()
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(GetTestDataPath("sample-audio.wav"));

        string hash = await MediaScanService.ComputeQuickHeaderFooterHashAsync(file);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public async Task ComputeMD5Async_ReturnsHashForRealAudioSample()
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(GetTestDataPath("sample-audio.wav"));

        string hash = await MediaScanService.ComputeMD5Async(file);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public async Task ComputePHashAsync_ReturnsHashForRealImageSample()
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(GetTestDataPath("sample-image.png"));

        ulong? hash = await MediaScanService.ComputePHashAsync(file);

        Assert.True(hash.HasValue);
    }

    private static string GetTestDataPath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "TestData",
            "MediaRealWorld",
            fileName));
    }
}

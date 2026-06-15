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
    public async Task ComputeQuickHeaderFooterHashAsync_ReturnsHashForRealImageSample()
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(GetTestDataPath("sample-image.png"));

        string hash = await MediaScanService.ComputeQuickHeaderFooterHashAsync(file);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public async Task ComputeMD5Async_ReturnsHashForRealImageSample()
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(GetTestDataPath("sample-image.png"));

        string hash = await MediaScanService.ComputeMD5Async(file);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public async Task ComputeDHashAsync_ReturnsHashForRealImageSample()
    {
        string path = GetTestDataPath("sample-image.png");

        ulong? hash = await MediaScanService.ComputeDHashAsync(path);

        Assert.True(hash.HasValue);
    }

    private static string GetTestDataPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidatePath = Path.Combine(directory.FullName, "TestData", "MediaRealWorld", fileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Test data file was not found: {fileName}");
    }
}

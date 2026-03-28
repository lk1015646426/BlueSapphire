using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class ImageProcessingServiceTests : IDisposable
{
    private readonly ImageProcessingService _service = new();
    private readonly string _tempDirectory;

    public ImageProcessingServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BlueSapphireTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void BuildOutputPath_AvoidsNameCollision()
    {
        string sourcePath = Path.Combine(_tempDirectory, "cover.png");
        File.WriteAllText(sourcePath, "stub");

        string firstOutputPath = _service.BuildOutputPath(sourcePath, "_crop", ".png");
        File.WriteAllText(firstOutputPath, "stub");

        string secondOutputPath = _service.BuildOutputPath(sourcePath, "_crop", ".png");

        Assert.EndsWith("cover_crop.png", firstOutputPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("cover_crop_01.png", secondOutputPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(4000u, 3000u, 1920u, 1920u, 1440u)]
    [InlineData(3000u, 4000u, 1920u, 1440u, 1920u)]
    public void CalculateResizeDimensions_PreservesAspectRatio(
        uint sourceWidth,
        uint sourceHeight,
        uint longEdge,
        uint expectedWidth,
        uint expectedHeight)
    {
        var result = ImageProcessingService.CalculateResizeDimensions(sourceWidth, sourceHeight, longEdge);

        Assert.Equal((expectedWidth, expectedHeight), result);
    }

    [Theory]
    [InlineData(4000u, 3000u, 1d, 500u, 0u, 3000u, 3000u)]
    [InlineData(4000u, 3000u, 16d / 9d, 0u, 375u, 4000u, 2250u)]
    public void CalculateCenteredCropFrame_ReturnsExpectedCenteredBounds(
        uint sourceWidth,
        uint sourceHeight,
        double aspectRatio,
        uint expectedX,
        uint expectedY,
        uint expectedWidth,
        uint expectedHeight)
    {
        var frame = ImageProcessingService.CalculateCenteredCropFrame(sourceWidth, sourceHeight, aspectRatio);

        Assert.Equal(new ImageCropFrame(expectedX, expectedY, expectedWidth, expectedHeight), frame);
    }

    [Theory]
    [InlineData(800u, 600u, 1d, 800u, 600u)]
    [InlineData(800u, 600u, 1.5d, 1200u, 900u)]
    [InlineData(1200u, 800u, 2d, 2400u, 1600u)]
    public void CalculateEnhancedDimensions_ReturnsExpectedSize(
        uint sourceWidth,
        uint sourceHeight,
        double scaleFactor,
        uint expectedWidth,
        uint expectedHeight)
    {
        var result = ImageProcessingService.CalculateEnhancedDimensions(sourceWidth, sourceHeight, scaleFactor);

        Assert.Equal((expectedWidth, expectedHeight), result);
    }

    [Theory]
    [InlineData("SmartFix", ImageEnhancementPreset.SmartFix)]
    [InlineData("detailboost", ImageEnhancementPreset.DetailBoost)]
    [InlineData("LowLight", ImageEnhancementPreset.LowLight)]
    public void TryParseEnhancementPreset_ParsesKnownValues(string presetKey, ImageEnhancementPreset expectedPreset)
    {
        bool parsed = _service.TryParseEnhancementPreset(presetKey, out var preset);

        Assert.True(parsed);
        Assert.Equal(expectedPreset, preset);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }
}

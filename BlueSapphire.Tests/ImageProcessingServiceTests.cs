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

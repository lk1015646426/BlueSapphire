using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class MediaTagServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly MediaTagService _service;

    public MediaTagServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BlueSapphireTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _service = new MediaTagService(_tempDirectory);
    }

    [Fact]
    public void ParseTags_NormalizesTrimmedDistinctValues()
    {
        var tags = _service.ParseTags(" 旅行, 海边，旅行 ; 精选 \n 壁纸 ");

        Assert.Equal(new[] { "旅行", "海边", "精选", "壁纸" }, tags);
    }

    [Fact]
    public async Task ReplaceAndMoveTagsAsync_PersistsExpectedEntries()
    {
        string sourcePath = Path.Combine(_tempDirectory, "cover.jpg");
        string destinationPath = Path.Combine(_tempDirectory, "cover_renamed.jpg");

        var updateResult = await _service.ReplaceTagsAsync(sourcePath, new[] { "旅行", "海边" });
        Assert.True(updateResult.Success);

        var storedTags = await _service.GetTagsAsync(sourcePath);
        Assert.Equal(new[] { "旅行", "海边" }, storedTags);

        await _service.ReplaceTagsAsync(destinationPath, new[] { "精选" });
        await _service.MoveTagsAsync(sourcePath, destinationPath);

        var movedTags = await _service.GetTagsAsync(destinationPath);
        Assert.Equal(3, movedTags.Count);
        Assert.Contains("精选", movedTags);
        Assert.Contains("旅行", movedTags);
        Assert.Contains("海边", movedTags);

        var originalTags = await _service.GetTagsAsync(sourcePath);
        Assert.Empty(originalTags);
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

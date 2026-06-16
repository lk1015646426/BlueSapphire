using Microsoft.Extensions.Logging.Abstractions;
using BlueSapphire.Services;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Tests;

public class MediaTagServiceTests : IDisposable
{
    private readonly string _dataDir;
    private readonly MediaTagService _service;

    public MediaTagServiceTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "BlueSapphireMediaTagTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _service = new MediaTagService(NullLogger<MediaTagService>.Instance, _dataDir);
    }

    // ================================================================
    // Test 1: ParseTags 解析逗号分隔的标签
    // ================================================================
    [Fact]
    public void ParseTags_ParsesCommaSeparatedTags()
    {
        var tags = _service.ParseTags("风景, 旅行, 2024");

        Assert.Equal(3, tags.Count);
        Assert.Contains("风景", tags);
        Assert.Contains("旅行", tags);
        Assert.Contains("2024", tags);
    }

    // ================================================================
    // Test 2: ParseTags 空白字符串返回空列表
    // ================================================================
    [Fact]
    public void ParseTags_ReturnsEmptyForNull()
    {
        var tags = _service.ParseTags(null);

        Assert.Empty(tags);
    }

    // ================================================================
    // Test 3: ParseTags 去除空白和空项
    // ================================================================
    [Fact]
    public void ParseTags_TrimsWhitespaceAndSkipsEmpty()
    {
        var tags = _service.ParseTags("  风景  , , 旅行 ");

        Assert.Equal(2, tags.Count);
        Assert.Contains("风景", tags);
        Assert.Contains("旅行", tags);
    }

    // ================================================================
    // Test 4: GetTagsAsync 新文件返回空标签
    // ================================================================
    [Fact]
    public async Task GetTagsAsync_ReturnsEmptyForUntaggedFile()
    {
        string filePath = Path.Combine(_dataDir, "untagged.jpg");
        await File.WriteAllTextAsync(filePath, "test image content");

        var tags = await _service.GetTagsAsync(filePath);

        Assert.Empty(tags);
    }

    // ================================================================
    // Test 5: ReplaceTagsAsync 设置并读取标签
    // ================================================================
    [Fact]
    public async Task ReplaceTagsAsync_SetsAndReadsTags()
    {
        string filePath = Path.Combine(_dataDir, "tagged.jpg");
        await File.WriteAllTextAsync(filePath, "test image content");

        var result = await _service.ReplaceTagsAsync(filePath, new[] { "风景", "人像" });

        Assert.True(result.Success);
        var tags = await _service.GetTagsAsync(filePath);
        Assert.Equal(2, tags.Count);
        Assert.Contains("风景", tags);
        Assert.Contains("人像", tags);
    }

    // ================================================================
    // Test 6: RemoveTagsAsync 清除标签
    // ================================================================
    [Fact]
    public async Task RemoveTagsAsync_ClearsTags()
    {
        string filePath = Path.Combine(_dataDir, "to_remove.jpg");
        await File.WriteAllTextAsync(filePath, "test image content");
        await _service.ReplaceTagsAsync(filePath, new[] { "待删除" });

        await _service.RemoveTagsAsync(new[] { filePath });

        var tags = await _service.GetTagsAsync(filePath);
        Assert.Empty(tags);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, true);
        }
    }
}

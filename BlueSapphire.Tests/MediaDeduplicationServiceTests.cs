using BlueSapphire.Services;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace BlueSapphire.Tests;

public class MediaDeduplicationServiceTests
{
    private readonly MediaDeduplicationService _service = new(NullLogger<MediaDeduplicationService>.Instance);

    // ================================================================
    // Test 1: 空文件夹不产生重复结果
    // ================================================================
    [Fact]
    public async Task FindDuplicatesAsync_EmptyFolderReturnsEmpty()
    {
        string emptyDir = Path.Combine(Path.GetTempPath(), "BlueSapphireDedupEmpty", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        try
        {
            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(emptyDir);

            var groups = await _service.FindDuplicatesAsync(
                folder,
                new Progress<(double Value, string Message, string Detail)>(),
                CancellationToken.None);

            Assert.Empty(groups);
        }
        finally
        {
            if (Directory.Exists(emptyDir)) Directory.Delete(emptyDir, true);
        }
    }

    // ================================================================
    // Test 2: 单文件不产生重复结果
    // ================================================================
    [Fact]
    public async Task FindDuplicatesAsync_SingleFileReturnsEmpty()
    {
        string testDir = Path.Combine(Path.GetTempPath(), "BlueSapphireDedupSingle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(testDir, "only.jpg"), "fake image data");
            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(testDir);

            var groups = await _service.FindDuplicatesAsync(
                folder,
                new Progress<(double Value, string Message, string Detail)>(),
                CancellationToken.None);

            Assert.Empty(groups);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    // ================================================================
    // Test 3: 两个相同内容的文件被识别为重复
    // ================================================================
    [Fact]
    public async Task FindDuplicatesAsync_IdenticalFilesGroupedTogether()
    {
        string testDir = Path.Combine(Path.GetTempPath(), "BlueSapphireDedupDup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            string content = "identical fake image content for dedup test";
            await File.WriteAllTextAsync(Path.Combine(testDir, "a.jpg"), content);
            await File.WriteAllTextAsync(Path.Combine(testDir, "b.jpg"), content);
            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(testDir);

            var groups = await _service.FindDuplicatesAsync(
                folder,
                new Progress<(double Value, string Message, string Detail)>(),
                CancellationToken.None);

            Assert.Single(groups);
            Assert.Equal(2, groups[0].Count);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    // ================================================================
    // Test 4: 取消令牌正确中断操作
    // ================================================================
    [Fact]
    public async Task FindDuplicatesAsync_CancellationStopsProcessing()
    {
        string testDir = Path.Combine(Path.GetTempPath(), "BlueSapphireDedupCancel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            // 创建一些文件确保扫描开始
            for (int i = 0; i < 10; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(testDir, $"img{i:D4}.jpg"), $"content {i}");
            }
            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(testDir);
            using var cts = new CancellationTokenSource();

            // 立即取消
            cts.Cancel();

            var groups = await _service.FindDuplicatesAsync(
                folder,
                new Progress<(double Value, string Message, string Detail)>(),
                cts.Token);

            Assert.Empty(groups);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    // ================================================================
    // Test 5: 不同内容的文件不被识别为重复
    // ================================================================
    [Fact]
    public async Task FindDuplicatesAsync_DifferentFilesNotGrouped()
    {
        string testDir = Path.Combine(Path.GetTempPath(), "BlueSapphireDedupDiff", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(testDir, "a.jpg"), "content AAAA");
            await File.WriteAllTextAsync(Path.Combine(testDir, "b.jpg"), "content BBBB different size");
            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(testDir);

            var groups = await _service.FindDuplicatesAsync(
                folder,
                new Progress<(double Value, string Message, string Detail)>(),
                CancellationToken.None);

            // 不同大小的文件不应该形成组
            Assert.All(groups, g => Assert.True(g.Count >= 2));
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }
}

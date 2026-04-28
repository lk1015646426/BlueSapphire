using BlueSapphire.Models;

namespace BlueSapphire.Tests;

public class ImageItemTests
{
    [Fact]
    public void ImageMetadataText_UsesResolutionFormatAndBitDepth()
    {
        var item = new ImageItem
        {
            FileName = "cover.jpg",
            ImageWidth = 1920,
            ImageHeight = 1080,
            ImageFormat = "JPEG",
            ImageBitDepth = 24
        };

        Assert.Equal("1920x1080", item.MetadataPrimaryText);
        Assert.Equal("JPEG · 24-bit", item.MetadataSecondaryText);
    }

    [Fact]
    public void DetailLineText_UsesImageDateTakenWhenAvailable()
    {
        var item = new ImageItem
        {
            FileName = "cover.jpg",
            ImageDateTaken = new DateTimeOffset(2024, 5, 6, 7, 8, 0, TimeSpan.Zero)
        };

        Assert.Equal("拍摄于 2024-05-06 07:08", item.DetailLineText);
    }

    [Fact]
    public void MediaTypeAndExtensionLabels_FollowImageFileName()
    {
        var item = new ImageItem
        {
            FileName = "mix.photo.webp"
        };

        Assert.True(item.IsImageFile);
        Assert.Equal("图片", item.MediaTypeLabel);
        Assert.Equal("WEBP", item.FileExtensionLabel);
    }

    [Fact]
    public void CustomTagSummaryText_FormatsPreviewAndOverflowCount()
    {
        var item = new ImageItem
        {
            FileName = "cover.jpg",
            CustomTags = new[] { "旅行", "海边", "精选", "壁纸" }
        };

        Assert.True(item.HasCustomTags);
        Assert.Equal("#旅行 · #海边 · #精选 · +1", item.CustomTagSummaryText);
    }
}

using BlueSapphire.Services;
using Windows.Graphics.Imaging;

namespace BlueSapphire.Tests;

public class ImageMetadataServiceTests
{
    [Theory]
    [InlineData("cover.jpg", "JPEG")]
    [InlineData("cover.jpeg", "JPEG")]
    [InlineData("cover.png", "PNG")]
    [InlineData("cover.bmp", "BMP")]
    [InlineData("cover.heic", "HEIC")]
    [InlineData("cover.unknown", "UNKNOWN")]
    public void GetFormatDisplayName_ReturnsExpectedValue(string fileName, string expected)
    {
        Assert.Equal(expected, ImageMetadataService.GetFormatDisplayName(fileName));
    }

    [Theory]
    [InlineData(BitmapPixelFormat.Bgra8, 32)]
    [InlineData(BitmapPixelFormat.Gray8, 8)]
    [InlineData(BitmapPixelFormat.Rgba16, 16)]
    [InlineData(BitmapPixelFormat.Yuy2, 16)]
    public void GetBitsPerPixel_ReturnsMappedValue(BitmapPixelFormat pixelFormat, ushort expectedBitsPerPixel)
    {
        Assert.Equal(expectedBitsPerPixel, ImageMetadataService.GetBitsPerPixel(pixelFormat));
    }
}

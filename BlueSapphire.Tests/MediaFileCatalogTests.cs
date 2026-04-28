using BlueSapphire.Helpers;

namespace BlueSapphire.Tests;

public class MediaFileCatalogTests
{
    [Theory]
    [InlineData("photo.JPG")]
    [InlineData("clip.heic")]
    [InlineData("poster.webp")]
    public void IsImage_RecognizesSupportedExtensions(string fileName)
    {
        Assert.True(MediaFileCatalog.IsImage(fileName));
        Assert.True(MediaFileCatalog.IsSupported(fileName));
    }

    [Theory]
    [InlineData("voice.m4a")]
    [InlineData("report.docx")]
    [InlineData("sheet.csv")]
    [InlineData("slides.pptm")]
    [InlineData("movie.mkv")]
    [InlineData("clip.mp4")]
    [InlineData("archive.zip")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSupported_RejectsNonImageExtensions(string? fileName)
    {
        Assert.False(MediaFileCatalog.IsSupported(fileName));
    }
}

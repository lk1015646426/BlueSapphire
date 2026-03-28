using BlueSapphire.Helpers;

namespace BlueSapphire.Tests;

public class MediaFileCatalogTests
{
    [Theory]
    [InlineData("photo.JPG")]
    [InlineData("clip.heic")]
    public void IsImage_RecognizesSupportedExtensions(string fileName)
    {
        Assert.True(MediaFileCatalog.IsImage(fileName));
    }

    [Theory]
    [InlineData("voice.m4a")]
    [InlineData("report.docx")]
    [InlineData("sheet.csv")]
    [InlineData("slides.pptm")]
    public void IsSupported_RecognizesAllConfiguredTypes(string fileName)
    {
        Assert.True(MediaFileCatalog.IsSupported(fileName));
    }

    [Theory]
    [InlineData("movie.mkv")]
    [InlineData("clip.mp4")]
    [InlineData("archive.zip")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSupported_RejectsUnknownExtensions(string? fileName)
    {
        Assert.False(MediaFileCatalog.IsSupported(fileName));
    }
}

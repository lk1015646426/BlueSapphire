using BlueSapphire.Models;

namespace BlueSapphire.Tests;

public class ImageItemTests
{
    [Fact]
    public void MetadataSecondaryText_UsesAudioBitrateAndSampleRateWhenAvailable()
    {
        var item = new ImageItem
        {
            FileName = "track.mp3",
            FileSize = 1024,
            AudioBitrate = 320000,
            AudioSampleRate = 44100
        };

        Assert.Equal("320 kbps · 44.1 kHz", item.MetadataSecondaryText);
    }

    [Fact]
    public void MetadataSecondaryText_FallsBackToFileSizeWhenAudioTechnicalMetadataMissing()
    {
        var item = new ImageItem
        {
            FileName = "track.mp3",
            FileSize = 2048
        };

        Assert.Equal(item.FileSizeString, item.MetadataSecondaryText);
    }

    [Fact]
    public void DetailLineText_UsesAlbumArtistWhenArtistMissing()
    {
        var item = new ImageItem
        {
            FileName = "track.mp3",
            AudioAlbumArtist = "Album Artist",
            AudioAlbum = "Album"
        };

        Assert.Equal("Album Artist · Album", item.DetailLineText);
    }

    [Fact]
    public void DetailLineText_FallsBackToGenreWhenArtistAndAlbumMissing()
    {
        var item = new ImageItem
        {
            FileName = "track.mp3",
            AudioGenre = "Jazz"
        };

        Assert.Equal("Jazz", item.DetailLineText);
    }

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
    public void MediaTypeAndExtensionLabels_FollowFileName()
    {
        var item = new ImageItem
        {
            FileName = "mix.track.flac"
        };

        Assert.True(item.IsAudioFile);
        Assert.Equal("音频", item.MediaTypeLabel);
        Assert.Equal("FLAC", item.FileExtensionLabel);
    }

    [Fact]
    public void HasAudioAssetBadges_ReflectsCoverArtOrLyrics()
    {
        var item = new ImageItem
        {
            FileName = "track.mp3"
        };

        Assert.False(item.HasAudioAssetBadges);

        item.AudioLyrics = "Lyrics";
        Assert.True(item.HasAudioLyrics);
        Assert.True(item.HasAudioAssetBadges);

        item.AudioLyrics = null;
        item.HasEmbeddedCoverArt = true;
        Assert.True(item.HasAudioAssetBadges);
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

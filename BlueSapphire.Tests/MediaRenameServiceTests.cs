using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class MediaRenameServiceTests
{
    private readonly MediaRenameService _service = new();

    [Theory]
    [InlineData("IMG_20240305_102233.jpg", 2024, 3, 5, 10, 22, 33)]
    [InlineData("旅行-2024年3月5日.png", 2024, 3, 5, 0, 0, 0)]
    [InlineData("2024-03-05 102233.mp4", 2024, 3, 5, 10, 22, 33)]
    public void ParseTimestampFromFileName_ExtractsExpectedDate(
        string fileName,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second)
    {
        var result = _service.ParseTimestampFromFileName(fileName);

        Assert.Equal(year, result.Year);
        Assert.Equal(month, result.Month);
        Assert.Equal(day, result.Day);
        Assert.Equal(hour, result.Hour);
        Assert.Equal(minute, result.Minute);
        Assert.Equal(second, result.Second);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("2024-99-99.jpg")]
    [InlineData("")]
    public void ParseTimestampFromFileName_ReturnsMinValueForInvalidNames(string fileName)
    {
        Assert.Equal(DateTimeOffset.MinValue, _service.ParseTimestampFromFileName(fileName));
    }

    [Theory]
    [InlineData(AudioRenamePattern.Title, "Song Title")]
    [InlineData(AudioRenamePattern.ArtistTitle, "Artist - Song Title")]
    [InlineData(AudioRenamePattern.TrackTitle, "03 - Song Title")]
    [InlineData(AudioRenamePattern.AlbumTrackTitle, "Album - 03 - Song Title")]
    public void TryBuildAudioMetadataBaseName_BuildsExpectedNames(AudioRenamePattern pattern, string expectedName)
    {
        var metadata = new AudioMetadataInfo(
            TimeSpan.FromMinutes(4),
            "Artist",
            "Album",
            "Song Title",
            3,
            2024,
            320000,
            44100);

        bool built = _service.TryBuildAudioMetadataBaseName(metadata, pattern, out string actualName);

        Assert.True(built);
        Assert.Equal(expectedName, actualName);
    }

    [Fact]
    public void TryBuildAudioMetadataBaseName_ReturnsFalseWhenTitleMissing()
    {
        var metadata = new AudioMetadataInfo(
            TimeSpan.FromMinutes(4),
            "Artist",
            "Album",
            null,
            3,
            2024,
            320000,
            44100);

        bool built = _service.TryBuildAudioMetadataBaseName(metadata, AudioRenamePattern.ArtistTitle, out string actualName);

        Assert.False(built);
        Assert.Equal(string.Empty, actualName);
    }

    [Theory]
    [InlineData("Song Title.mp3", AudioRenamePattern.Title, "Song Title", null, null, null)]
    [InlineData("Artist - Song Title.flac", AudioRenamePattern.ArtistTitle, "Song Title", "Artist", null, null)]
    [InlineData("03 - Song Title.wav", AudioRenamePattern.TrackTitle, "Song Title", null, null, 3)]
    [InlineData("Album - 08 - Song Title.m4a", AudioRenamePattern.AlbumTrackTitle, "Song Title", null, "Album", 8)]
    public void TryBuildAudioTagRequestFromFileName_ParsesExpectedMetadata(
        string fileName,
        AudioRenamePattern pattern,
        string expectedTitle,
        string? expectedArtist,
        string? expectedAlbum,
        int? expectedTrackNumber)
    {
        bool built = _service.TryBuildAudioTagRequestFromFileName(fileName, pattern, out var request);

        Assert.True(built);
        Assert.True(request.ApplyTitle);
        Assert.Equal(expectedTitle, request.Title);
        Assert.Equal(expectedArtist != null, request.ApplyArtist);
        Assert.Equal(expectedArtist, request.Artist);
        Assert.Equal(expectedAlbum != null, request.ApplyAlbum);
        Assert.Equal(expectedAlbum, request.Album);
        Assert.Equal(expectedTrackNumber.HasValue, request.ApplyTrackNumber);
        Assert.Equal(expectedTrackNumber.HasValue ? (uint?)expectedTrackNumber.Value : null, request.TrackNumber);
    }

    [Theory]
    [InlineData("SongTitle.mp3", AudioRenamePattern.ArtistTitle)]
    [InlineData("Artist - Song.mp3", AudioRenamePattern.TrackTitle)]
    [InlineData("Album - xx - Song.mp3", AudioRenamePattern.AlbumTrackTitle)]
    public void TryBuildAudioTagRequestFromFileName_ReturnsFalseForMismatchedPattern(string fileName, AudioRenamePattern pattern)
    {
        bool built = _service.TryBuildAudioTagRequestFromFileName(fileName, pattern, out _);

        Assert.False(built);
    }
}

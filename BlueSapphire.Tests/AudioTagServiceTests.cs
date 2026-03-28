using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class AudioTagServiceTests
{
    private readonly AudioTagService _service = new();

    [Fact]
    public void BuildPropertyMap_BuildsExpectedEntries()
    {
        var request = new AudioTagEditRequest(
            ApplyTitle: true,
            Title: "Song Title",
            ApplyArtist: true,
            Artist: "Artist",
            ApplyAlbum: true,
            Album: "Album",
            ApplyTrackNumber: true,
            TrackNumber: 7,
            ApplyYear: true,
            Year: 2024,
            ApplyAlbumArtist: true,
            AlbumArtist: "Album Artist",
            ApplyComposer: true,
            Composer: "Composer",
            ApplyGenre: true,
            Genre: "Pop",
            ApplyDiscNumber: true,
            DiscNumber: 2,
            ApplyComment: true,
            Comment: "Comment",
            ApplyLyrics: true,
            Lyrics: "Lyrics");

        var map = _service.BuildPropertyMap(request);

        Assert.Equal("Song Title", map[AudioTagService.TitlePropertyKey]);
        Assert.Equal("Album", map[AudioTagService.AlbumPropertyKey]);
        Assert.Equal((uint)7, map[AudioTagService.TrackNumberPropertyKey]);
        Assert.Equal((uint)2024, map[AudioTagService.YearPropertyKey]);
        Assert.Equal((uint)2, map[AudioTagService.DiscNumberPropertyKey]);
        Assert.Equal("Comment", map[AudioTagService.CommentPropertyKey]);
        Assert.Equal("Lyrics", map[AudioTagService.LyricsPropertyKey]);

        var artists = Assert.IsType<string[]>(map[AudioTagService.ArtistPropertyKey]);
        Assert.Single(artists);
        Assert.Equal("Artist", artists[0]);

        var albumArtists = Assert.IsType<string[]>(map[AudioTagService.AlbumArtistPropertyKey]);
        Assert.Single(albumArtists);
        Assert.Equal("Album Artist", albumArtists[0]);

        var composers = Assert.IsType<string[]>(map[AudioTagService.ComposerPropertyKey]);
        Assert.Single(composers);
        Assert.Equal("Composer", composers[0]);

        var genres = Assert.IsType<string[]>(map[AudioTagService.GenrePropertyKey]);
        Assert.Single(genres);
        Assert.Equal("Pop", genres[0]);
    }

    [Fact]
    public void BuildPropertyMap_UsesEmptyValuesToClearFields()
    {
        var request = new AudioTagEditRequest(
            ApplyTitle: true,
            Title: "",
            ApplyArtist: true,
            Artist: " ",
            ApplyAlbum: false,
            Album: null,
            ApplyTrackNumber: true,
            TrackNumber: null,
            ApplyYear: true,
            Year: null,
            ApplyAlbumArtist: true,
            AlbumArtist: " ",
            ApplyComposer: true,
            Composer: "",
            ApplyGenre: true,
            Genre: null,
            ApplyDiscNumber: true,
            DiscNumber: null,
            ApplyComment: true,
            Comment: " ",
            ApplyLyrics: true,
            Lyrics: "");

        var map = _service.BuildPropertyMap(request);

        Assert.Equal(string.Empty, map[AudioTagService.TitlePropertyKey]);
        Assert.Equal((uint)0, map[AudioTagService.TrackNumberPropertyKey]);
        Assert.Equal((uint)0, map[AudioTagService.YearPropertyKey]);
        Assert.Equal((uint)0, map[AudioTagService.DiscNumberPropertyKey]);
        Assert.Equal(string.Empty, map[AudioTagService.CommentPropertyKey]);
        Assert.Equal(string.Empty, map[AudioTagService.LyricsPropertyKey]);

        var artists = Assert.IsType<string[]>(map[AudioTagService.ArtistPropertyKey]);
        Assert.Empty(artists);

        var albumArtists = Assert.IsType<string[]>(map[AudioTagService.AlbumArtistPropertyKey]);
        Assert.Empty(albumArtists);

        var composers = Assert.IsType<string[]>(map[AudioTagService.ComposerPropertyKey]);
        Assert.Empty(composers);

        var genres = Assert.IsType<string[]>(map[AudioTagService.GenrePropertyKey]);
        Assert.Empty(genres);

        Assert.DoesNotContain(AudioTagService.AlbumPropertyKey, map.Keys);
    }

    [Theory]
    [InlineData("image/jpeg", null, ".jpg")]
    [InlineData("image/png", null, ".png")]
    [InlineData(null, "cover.bmp", ".bmp")]
    [InlineData(null, null, ".jpg")]
    public void GetCoverFileExtension_ReturnsExpectedExtension(string? mimeType, string? fileName, string expectedExtension)
    {
        string extension = _service.GetCoverFileExtension(mimeType, fileName);

        Assert.Equal(expectedExtension, extension);
    }

    [Fact]
    public void BuildExportOutputPath_AvoidsFileNameCollision()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "BlueSapphireTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string audioPath = Path.Combine(tempDirectory, "track.mp3");
            File.WriteAllText(audioPath, "stub");

            string firstOutputPath = _service.BuildExportOutputPath(audioPath, "_cover", ".jpg");
            File.WriteAllText(firstOutputPath, "stub");

            string secondOutputPath = _service.BuildExportOutputPath(audioPath, "_cover", ".jpg");

            Assert.EndsWith("track_cover.jpg", firstOutputPath, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("track_cover_01.jpg", secondOutputPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void FindSidecarCoverArtPath_PrefersSameBaseNameBeforeAlbumCover()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "BlueSapphireTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string audioPath = Path.Combine(tempDirectory, "track.mp3");
            string sameNameCoverPath = Path.Combine(tempDirectory, "track.jpg");
            string albumCoverPath = Path.Combine(tempDirectory, "cover.jpg");
            File.WriteAllText(audioPath, "stub");
            File.WriteAllText(sameNameCoverPath, "stub");
            File.WriteAllText(albumCoverPath, "stub");

            string? result = _service.FindSidecarCoverArtPath(audioPath);

            Assert.Equal(sameNameCoverPath, result);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void FindSidecarLyricsPath_PrefersLrcBeforeTxt()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "BlueSapphireTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string audioPath = Path.Combine(tempDirectory, "track.mp3");
            string lrcPath = Path.Combine(tempDirectory, "track.lrc");
            string txtPath = Path.Combine(tempDirectory, "track.txt");
            File.WriteAllText(audioPath, "stub");
            File.WriteAllText(lrcPath, "[00:01.00]hello");
            File.WriteAllText(txtPath, "hello");

            string? result = _service.FindSidecarLyricsPath(audioPath);

            Assert.Equal(lrcPath, result);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}

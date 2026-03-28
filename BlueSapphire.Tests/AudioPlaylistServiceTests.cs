using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class AudioPlaylistServiceTests : IDisposable
{
    private readonly AudioPlaylistService _service = new();
    private readonly string _tempDirectory;

    public AudioPlaylistServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BlueSapphireTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void BuildOutputPath_AppendsExtensionAndAvoidsCollision()
    {
        string firstPath = _service.BuildOutputPath(_tempDirectory, "My Playlist");
        File.WriteAllText(firstPath, string.Empty);

        string secondPath = _service.BuildOutputPath(_tempDirectory, "My Playlist");

        Assert.EndsWith(".m3u8", firstPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("_01.m3u8", secondPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportAsync_WritesExtendedM3uWithRelativePaths()
    {
        string albumDirectory = Path.Combine(_tempDirectory, "Album");
        Directory.CreateDirectory(albumDirectory);

        string firstSong = Path.Combine(albumDirectory, "01.mp3");
        string secondSong = Path.Combine(albumDirectory, "02.mp3");
        File.WriteAllText(firstSong, "stub");
        File.WriteAllText(secondSong, "stub");

        string outputPath = Path.Combine(_tempDirectory, "mix.m3u8");
        var entries = new[]
        {
            new AudioPlaylistEntry(firstSong, "Artist - Song A", TimeSpan.FromSeconds(90)),
            new AudioPlaylistEntry(secondSong, "Artist - Song B", TimeSpan.FromSeconds(95))
        };

        var result = await _service.ExportAsync(entries, outputPath);
        string[] lines = await File.ReadAllLinesAsync(outputPath);

        Assert.True(result.UsesRelativePaths);
        Assert.Equal(2, result.EntryCount);
        Assert.Equal("#EXTM3U", lines[0]);
        Assert.Contains("#EXTINF:90,Artist - Song A", lines);
        Assert.Contains("Album/01.mp3", lines);
        Assert.Contains("#EXTINF:95,Artist - Song B", lines);
        Assert.Contains("Album/02.mp3", lines);
    }

    [Fact]
    public async Task ParsePlaylistPathsAsync_ResolvesRelativeAndAbsoluteEntries()
    {
        string albumDirectory = Path.Combine(_tempDirectory, "Album");
        Directory.CreateDirectory(albumDirectory);

        string firstSong = Path.Combine(albumDirectory, "01.mp3");
        string secondSong = Path.Combine(albumDirectory, "02.mp3");
        File.WriteAllText(firstSong, "stub");
        File.WriteAllText(secondSong, "stub");

        string playlistPath = Path.Combine(_tempDirectory, "mix.m3u8");
        await File.WriteAllLinesAsync(playlistPath, new[]
        {
            "#EXTM3U",
            "#EXTINF:90,Song A",
            "Album/01.mp3",
            "#EXTINF:95,Song B",
            secondSong,
            "Album/01.mp3"
        });

        var paths = await _service.ParsePlaylistPathsAsync(playlistPath);

        Assert.Equal(2, paths.Count);
        Assert.Equal(firstSong, paths[0]);
        Assert.Equal(secondSong, paths[1]);
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

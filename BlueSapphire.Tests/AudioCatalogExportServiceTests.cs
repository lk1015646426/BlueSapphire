using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class AudioCatalogExportServiceTests : IDisposable
{
    private readonly AudioCatalogExportService _service = new();
    private readonly string _tempDirectory;

    public AudioCatalogExportServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BlueSapphireTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void BuildOutputPath_AvoidsCollision()
    {
        string firstPath = _service.BuildOutputPath(_tempDirectory, "catalog");
        File.WriteAllText(firstPath, string.Empty);

        string secondPath = _service.BuildOutputPath(_tempDirectory, "catalog");

        Assert.EndsWith(".csv", firstPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("_01.csv", secondPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportAsync_WritesCsvWithEscapedFields()
    {
        string outputPath = Path.Combine(_tempDirectory, "catalog.csv");
        var entries = new[]
        {
            new AudioCatalogExportEntry(
                "track01.mp3",
                "Song, A",
                "Artist",
                "Album",
                "Album Artist",
                "Composer",
                "Pop",
                1,
                1,
                2026,
                TimeSpan.FromSeconds(95),
                320000,
                44100,
                true,
                @"C:\Music\track01.mp3")
        };

        var result = await _service.ExportAsync(entries, outputPath);
        string[] lines = await File.ReadAllLinesAsync(outputPath);

        Assert.Equal(1, result.EntryCount);
        Assert.Equal("FileName,Title,Artist,Album,AlbumArtist,Composer,Genre,Track,Disc,Year,Duration,Bitrate,SampleRate,HasCoverArt,Path", lines[0]);
        Assert.Contains("\"Song, A\"", lines[1]);
        Assert.Contains(",01:35,", lines[1]);
        Assert.Contains(",true,", lines[1]);
    }

    [Fact]
    public async Task ParseImportRowsAsync_ParsesExportedFields()
    {
        string csvPath = Path.Combine(_tempDirectory, "import.csv");
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "FileName,Title,Artist,Album,AlbumArtist,Composer,Genre,Track,Disc,Year,Duration,Bitrate,SampleRate,HasCoverArt,Path",
            "\"track01.mp3\",\"Song, A\",Artist,Album,Album Artist,Composer,Pop,1,2,2026,01:35,320000,44100,true,\"C:\\\\Music\\\\track01.mp3\""
        });

        var rows = await _service.ParseImportRowsAsync(csvPath);
        var row = Assert.Single(rows);

        Assert.Equal("track01.mp3", row.FileName);
        Assert.Equal("Song, A", row.Title);
        Assert.Equal("Artist", row.Artist);
        Assert.Equal("Album Artist", row.AlbumArtist);
        Assert.Equal((uint)1, row.TrackNumber);
        Assert.Equal((uint)2, row.DiscNumber);
        Assert.Equal((uint)2026, row.Year);
        Assert.True(row.HasGenre);
        Assert.Equal(@"C:\\Music\\track01.mp3", row.Path);
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

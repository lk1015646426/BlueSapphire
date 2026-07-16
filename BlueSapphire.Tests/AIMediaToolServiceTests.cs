using BlueSapphire.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlueSapphire.Tests;

public sealed class AIMediaToolServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "BlueSapphire.Tests",
        "AIMedia",
        Guid.NewGuid().ToString("N"));

    public AIMediaToolServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task AnalyzeFolderAsync_FindsSha256ExactDuplicatesWithoutChangingFiles()
    {
        byte[] duplicate = Enumerable.Repeat((byte)42, 4096).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_root, "a.png"), duplicate);
        await File.WriteAllBytesAsync(Path.Combine(_root, "b.png"), duplicate);
        await File.WriteAllBytesAsync(Path.Combine(_root, "different.png"), Enumerable.Repeat((byte)7, 2048).ToArray());

        using var taskCenter = new AITaskCenterService(Path.Combine(_root, "tasks"));
        var shared = new AISharedContextService();
        var service = new AIMediaToolService(
            taskCenter,
            shared,
            new NativeFileService(),
            new MediaTagService(NullLogger<MediaTagService>.Instance, Path.Combine(_root, "tags")));

        var result = await service.AnalyzeFolderAsync(_root, recursive: false, CancellationToken.None);

        Assert.Equal(3, result.FileCount);
        Assert.Single(result.ExactDuplicateGroups);
        Assert.Equal(2, result.ExactDuplicateGroups[0].Count);
        Assert.True(File.Exists(Path.Combine(_root, "a.png")));
        Assert.True(File.Exists(Path.Combine(_root, "b.png")));
        Assert.NotNull(shared.GetMediaAnalysis());
    }

    [Fact]
    public void BuildOrganizationPreview_IsDryRunAndCreatesNoDirectories()
    {
        string imagePath = Path.Combine(_root, "photo.jpg");
        File.WriteAllBytes(imagePath, [1, 2, 3]);
        using var taskCenter = new AITaskCenterService(Path.Combine(_root, "tasks"));
        var service = new AIMediaToolService(
            taskCenter,
            new AISharedContextService(),
            new NativeFileService(),
            new MediaTagService(NullLogger<MediaTagService>.Instance, Path.Combine(_root, "tags")));

        var preview = service.BuildOrganizationPreview(_root, recursive: false);

        Assert.Single(preview.Moves);
        Assert.True(File.Exists(imagePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(preview.Moves[0].DestinationPath)!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

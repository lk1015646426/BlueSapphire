using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerSpaceAnalysisServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BlueSapphireSpaceAnalysisTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AnalyzeAsync_ReturnsLargeDirectoryAndLargeFileItems()
    {
        string candidateRoot = Path.Combine(_root, "Projects");
        Directory.CreateDirectory(candidateRoot);

        string largeFile = Path.Combine(candidateRoot, "archive.bin");
        await File.WriteAllBytesAsync(largeFile, new byte[8192]);
        File.SetLastWriteTimeUtc(largeFile, DateTime.UtcNow.AddDays(-10));

        CleanerSpaceAnalysisService service = new(
            new CleanerRiskEvaluator(),
            new CleanerLockService(),
            largeDirectoryThresholdBytes: 1024,
            largeFileThresholdBytes: 2048,
            maxCandidateDirectoriesPerRoot: 8,
            maxVisitedDirectories: 64,
            maxVisitedFiles: 512);

        List<CleanerScanItem> results = await service.AnalyzeAsync(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [_root],
            CancellationToken.None);

        Assert.Contains(results, item => item.RuleId == "analysis_large_directory" &&
                                         string.Equals(item.Path, candidateRoot, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, item => item.RuleId == "analysis_large_file" &&
                                         string.Equals(item.Path, largeFile, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

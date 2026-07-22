using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerOrphanResidueServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BlueSapphireResidueTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AnalyzeOrphanedCacheDirectories_ReturnsCandidateForUnknownOwner()
    {
        string localRoot = Path.Combine(_root, "Local");
        string cachePath = Path.Combine(localRoot, "GhostApp", "Cache");
        Directory.CreateDirectory(cachePath);

        for (int i = 0; i < 5; i++)
        {
            string filePath = Path.Combine(cachePath, $"cache-{i}.bin");
            File.WriteAllBytes(filePath, new byte[4096]);
            File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddDays(-40));
        }

        Directory.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddDays(-40));
        CleanerOrphanResidueService service = new(new CleanerLockService());

        List<CleanerScanItem> results = service.AnalyzeOrphanedCacheDirectories(
            [localRoot],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            minimumSizeBytes: 1024,
            minimumFileCount: 1,
            minimumAge: TimeSpan.FromDays(1));

        CleanerScanItem item = Assert.Single(results);
        Assert.Equal("orphan_leftover", item.Category);
        Assert.Contains("GhostApp", item.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Single(item.TargetPaths);
        Assert.True(item.ViewOnly);
        Assert.False(item.CanSelect);
        Assert.Equal(CleanerExecutionMode.None, item.ExecutionMode);
    }

    [Fact]
    public void AnalyzeOrphanedCacheDirectories_SkipsKnownInstalledOwner()
    {
        string localRoot = Path.Combine(_root, "LocalInstalled");
        string cachePath = Path.Combine(localRoot, "Chrome", "Cache");
        Directory.CreateDirectory(cachePath);
        string filePath = Path.Combine(cachePath, "cache.bin");
        File.WriteAllBytes(filePath, new byte[4096]);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddDays(-40));
        Directory.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddDays(-40));

        CleanerOrphanResidueService service = new(new CleanerLockService());
        List<CleanerScanItem> results = service.AnalyzeOrphanedCacheDirectories(
            [localRoot],
            new HashSet<string>(["chrome"], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            minimumSizeBytes: 1024,
            minimumFileCount: 1,
            minimumAge: TimeSpan.FromDays(1));

        Assert.Empty(results);
    }

    [Fact]
    public void FilterRootsToSelectedDrives_RemovesAppDataRootsOutsideSelectedDrive()
    {
        string appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string selectedRoot = Path.GetPathRoot(appDataRoot)!;
        string otherRoot = selectedRoot.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? @"D:\" : @"C:\";

        IReadOnlyList<string> rejected = CleanerOrphanResidueService.FilterRootsToSelectedDrives(
            [appDataRoot],
            [otherRoot]);
        IReadOnlyList<string> accepted = CleanerOrphanResidueService.FilterRootsToSelectedDrives(
            [appDataRoot],
            [selectedRoot]);

        Assert.Empty(rejected);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDataRoot)),
            Assert.Single(accepted),
            ignoreCase: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

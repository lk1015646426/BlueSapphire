using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerAnalysisPathPlannerTests
{
    [Fact]
    public void BuildAnalysisRoots_IncludesUserRootsForSystemDriveAndRawRootsForOtherDrives()
    {
        string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)!;
        IReadOnlyList<string> roots = CleanerAnalysisPathPlanner.BuildAnalysisRoots([systemDrive, @"D:\"]);

        Assert.Contains(roots, path => path.StartsWith(systemDrive, StringComparison.OrdinalIgnoreCase) && path.Contains(@"\Users\", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(roots, path => string.Equals(path.TrimEnd('\\'), @"D:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateCandidates_ReturnsSubdirectoriesForRegularRoot()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "BlueSapphireCleanerPlanner", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, "Projects"));
        Directory.CreateDirectory(Path.Combine(workspace, "Media"));

        try
        {
            IReadOnlyList<string> candidates = CleanerAnalysisPathPlanner.EnumerateCandidates(workspace);
            Assert.Contains(candidates, path => path.EndsWith("Projects", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(candidates, path => path.EndsWith("Media", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, true);
            }
        }
    }
}

using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public sealed class AICleanerRuleDraftServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "BlueSapphire.Tests",
        "AIRuleDraft",
        Guid.NewGuid().ToString("N"));

    public AICleanerRuleDraftServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Draft_IsAlwaysViewOnlyAndDoesNotBecomeActive()
    {
        string target = Path.Combine(_root, "project", "bin");
        Directory.CreateDirectory(target);
        var service = new AICleanerRuleDraftService(_root);

        CleanerRuleDefinition draft = service.BuildDraft(
            "构建缓存",
            target,
            ["*.log", "*.tmp"],
            includeSubdirectories: true);
        string savedPath = await service.SaveDraftAsync(draft);

        Assert.True(draft.ViewOnly);
        Assert.Equal(CleanerRiskLevel.High, draft.RiskLevel);
        Assert.Equal(CleanerExecutionMode.None, draft.ExecutionMode);
        Assert.False(draft.DefaultSelected);
        Assert.True(File.Exists(savedPath));
    }

    [Fact]
    public void Draft_RejectsDriveRoot()
    {
        var service = new AICleanerRuleDraftService(_root);
        string driveRoot = Path.GetPathRoot(_root)!;

        Assert.Throws<InvalidOperationException>(() =>
            service.BuildDraft("危险规则", driveRoot, [], true));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

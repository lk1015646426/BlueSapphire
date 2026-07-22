using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public sealed class MediaAIToolActionProviderTests
{
    [Fact]
    public void RegisterHandlers_ExposesOnlyMediaActions()
    {
        var provider = new MediaAIToolActionProvider(
            null!,
            null!,
            null!,
            null!);
        var registry = new AIToolActionHandlerRegistry();

        provider.RegisterHandlers(registry);

        Assert.Equal(
            new[]
            {
                "analyze_media_folder",
                "execute_exact_duplicate_cleanup",
                "execute_media_organization",
                "preview_media_organization"
            },
            registry.SnapshotNames());
        Assert.DoesNotContain("execute_cleanup", registry.SnapshotNames());
    }
}

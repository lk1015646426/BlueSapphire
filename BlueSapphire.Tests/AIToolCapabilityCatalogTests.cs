using System.Text.Json.Nodes;
using BlueSapphire.Models;
using BlueSapphire.Services;
using BlueSapphire.Interfaces;

namespace BlueSapphire.Tests;

public sealed class AIToolCapabilityCatalogTests
{
    [Fact]
    public void Replace_BuildsStableCapabilitySnapshotAndInfersOwnership()
    {
        var catalog = new AIToolCapabilityCatalog();
        catalog.Replace(
        [
            new ChatTool
            {
                Function = new ChatFunction
                {
                    Name = "analyze_media_folder",
                    Description = "只读分析媒体目录。",
                    Parameters = JsonNode.Parse("{\"type\":\"object\"}")
                }
            },
            new ChatTool
            {
                Function = new ChatFunction
                {
                    Name = "execute_cleanup",
                    Description = "执行清理。"
                }
            }
        ]);

        var capabilities = catalog.Snapshot();

        Assert.Equal(2, capabilities.Count);
        Assert.Equal("MediaManager", capabilities.Single(item => item.Name == "analyze_media_folder").ToolId);
        var cleanup = capabilities.Single(item => item.Name == "execute_cleanup");
        Assert.Equal(AIToolRiskLevel.Destructive, cleanup.RiskLevel);
        Assert.True(cleanup.RequiresConfirmation);
    }

    [Fact]
    public void Snapshot_ReturnsClonesSoCallersCannotMutateCatalog()
    {
        var catalog = new AIToolCapabilityCatalog();
        catalog.Replace(
        [
            new ChatTool
            {
                Function = new ChatFunction
                {
                    Name = "preview_media_organization",
                    Description = "生成预览。",
                    Parameters = JsonNode.Parse("{\"type\":\"object\"}")
                }
            }
        ]);

        var first = catalog.Snapshot()[0];
        first.Parameters!["type"] = "mutated";

        var second = catalog.Snapshot()[0];
        Assert.Equal("object", second.Parameters!["type"]!.GetValue<string>());
    }

    [Fact]
    public void BuildChatTools_PreservesModelFacingFunctionShape()
    {
        var catalog = new AIToolCapabilityCatalog();
        catalog.Replace(
        [
            new ChatTool
            {
                Function = new ChatFunction
                {
                    Name = "preview_media_organization",
                    Description = "只读预览。",
                    Parameters = JsonNode.Parse("{\"type\":\"object\"}")
                }
            }
        ]);

        var tool = Assert.Single(catalog.BuildChatTools());

        Assert.Equal("function", tool.Type);
        Assert.Equal("preview_media_organization", tool.Function.Name);
        Assert.Equal("只读预览。", tool.Function.Description);
        Assert.NotNull(tool.Function.Parameters);
    }

    [Fact]
    public void RegisterProvider_UsesProviderToolIdWhenCapabilityOmitsIt()
    {
        var catalog = new AIToolCapabilityCatalog();
        catalog.RegisterProvider(new TestProvider());

        var capability = Assert.Single(catalog.Snapshot());

        Assert.Equal("TestTool", capability.ToolId);
        Assert.Equal("test_action", capability.Name);
    }

    private sealed class TestProvider : IAIToolCapabilityProvider
    {
        public string ToolId => "TestTool";

        public IReadOnlyList<AIToolCapabilityDefinition> GetCapabilities() =>
        [
            new()
            {
                Name = "test_action",
                Description = "测试能力"
            }
        ];
    }
}

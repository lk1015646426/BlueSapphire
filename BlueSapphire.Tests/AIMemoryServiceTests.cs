using System.Text.Json;
using BlueSapphire.Services;
using BlueSapphire.Models;

namespace BlueSapphire.Tests;

public class AIMemoryServiceTests
{
    [Fact]
    public async Task AddMemoryRuleAsync_EncryptsAndDeduplicatesRules()
    {
        string root = CreateRoot();
        try
        {
            var service = new AIMemoryService(root);

            Assert.True(await service.AddMemoryRuleAsync("偏好简洁回答"));
            Assert.False(await service.AddMemoryRuleAsync("偏好简洁回答"));

            var reloaded = new AIMemoryService(root);
            Assert.Equal(new[] { "偏好简洁回答" }, await reloaded.GetMemoryRulesAsync());
            Assert.True(File.Exists(Path.Combine(root, "AIMemory.dat")));
            Assert.False(File.Exists(Path.Combine(root, "AIMemory.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StructuredMemory_SupportsScopeExpiryPauseEditAndDelete()
    {
        string root = CreateRoot();
        try
        {
            var service = new AIMemoryService(root);
            Assert.True(await service.AddMemoryEntryAsync(
                "媒体操作前先预览",
                AIMemoryScope.Media,
                DateTimeOffset.Now.AddDays(7),
                "测试"));

            AIMemoryEntry entry = Assert.Single(await service.GetEntriesAsync());
            Assert.Equal(AIMemoryScope.Media, entry.Scope);
            Assert.True(await service.UpdateEntryAsync(
                entry.Id,
                "媒体删除前必须预览",
                AIMemoryScope.Media,
                null,
                true));
            Assert.Equal(new[] { "媒体删除前必须预览" }, await service.GetMemoryRulesAsync());

            await service.SetPausedAsync(true);
            Assert.Empty(await service.GetMemoryRulesAsync());
            await service.SetPausedAsync(false);
            Assert.True(await service.RemoveEntryAsync(entry.Id));
            Assert.Empty(await service.GetEntriesAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GetMemoryRulesAsync_MigratesLegacyPlaintextFile()
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "AIMemory.json"),
                JsonSerializer.Serialize(new[] { "使用中文", "使用中文" }));
            var service = new AIMemoryService(root);

            List<string> rules = await service.GetMemoryRulesAsync();

            Assert.Equal(new[] { "使用中文" }, rules);
            Assert.True(File.Exists(Path.Combine(root, "AIMemory.dat")));
            Assert.False(File.Exists(Path.Combine(root, "AIMemory.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "BlueSapphire.Tests",
            "AIMemory",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

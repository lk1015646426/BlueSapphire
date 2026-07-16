using System.Text.Json;
using BlueSapphire.Services;

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

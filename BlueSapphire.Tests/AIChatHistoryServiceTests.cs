using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class AIChatHistoryServiceTests
{
    [Fact]
    public async Task SaveAndLoad_PersistsOnlyBoundedConversationMessages()
    {
        string root = CreateRoot();
        try
        {
            var service = new AIChatHistoryService(root);
            await service.SaveAsync(new[]
            {
                new ChatMessage { Role = "system", Content = "system secret" },
                new ChatMessage { Role = "tool", Content = "tool result" },
                new ChatMessage { Role = "user", Content = "hello" },
                new ChatMessage { Role = "assistant", Content = new string('a', 40_000) }
            });

            IReadOnlyList<ChatMessage> loaded = await service.LoadAsync();

            Assert.Equal(2, loaded.Count);
            Assert.Equal("user", loaded[0].Role);
            Assert.Equal("hello", loaded[0].Content);
            Assert.Equal("assistant", loaded[1].Role);
            Assert.Equal(32_000, loaded[1].Content?.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmptyForCorruptedHistory()
    {
        string root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "ai_chat_history.dat"),
                "not encrypted history");
            var service = new AIChatHistoryService(root);

            IReadOnlyList<ChatMessage> loaded = await service.LoadAsync();

            Assert.Empty(loaded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ClearAsync_RemovesSavedHistory()
    {
        string root = CreateRoot();
        try
        {
            var service = new AIChatHistoryService(root);
            await service.SaveAsync(new[]
            {
                new ChatMessage { Role = "user", Content = "hello" }
            });

            await service.ClearAsync();

            Assert.Empty(await service.LoadAsync());
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
            "AIChatHistory",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

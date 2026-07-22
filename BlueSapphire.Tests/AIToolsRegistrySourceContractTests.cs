namespace BlueSapphire.Tests;

public sealed class AIToolsRegistrySourceContractTests
{
    [Fact]
    public void ExecuteToolCallAsync_PropagatesCancellationBeforeGeneralFailureHandling()
    {
        string projectRoot = FindProjectRoot();
        string source = File.ReadAllText(Path.Combine(projectRoot, "Services", "AIToolsRegistry.cs"));
        int methodStart = source.IndexOf("public async Task<string> ExecuteToolCallAsync", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("private void RegisterBuiltInActionHandlers", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        string method = source[methodStart..methodEnd];
        int cancellationCatch = method.IndexOf("catch (OperationCanceledException)", StringComparison.Ordinal);
        int generalCatch = method.IndexOf("catch (Exception ex)", StringComparison.Ordinal);

        Assert.True(cancellationCatch >= 0, "ExecuteToolCallAsync 必须显式传播 OperationCanceledException。");
        Assert.True(generalCatch > cancellationCatch, "取消捕获必须位于通用异常捕获之前。");
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BlueSapphire.csproj")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("未找到 BlueSapphire 项目根目录。");
    }
}

using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class CleanerLaunchActionServiceTests
{
    [Fact]
    public void ConsumeRetryBatchId_ReturnsValueOnlyOnce()
    {
        CleanerLaunchActionService service = new(() => "--tool=CleanerAssistant --cleaner-retry-batch=batch-001");

        string? first = service.ConsumeRetryBatchId();
        string? second = service.ConsumeRetryBatchId();

        Assert.Equal("batch-001", first);
        Assert.Null(second);
    }

    [Fact]
    public void TokenizeArguments_SplitsKnownLaunchArguments()
    {
        IReadOnlyList<string> tokens = CleanerLaunchActionService.TokenizeArguments(
            "--tool=CleanerAssistant --cleaner-retry-batch=batch-002");

        Assert.Equal(2, tokens.Count);
        Assert.Contains("--tool=CleanerAssistant", tokens);
        Assert.Contains("--cleaner-retry-batch=batch-002", tokens);
    }
}

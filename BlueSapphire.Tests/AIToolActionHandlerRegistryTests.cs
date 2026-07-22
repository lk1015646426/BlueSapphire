using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public sealed class AIToolActionHandlerRegistryTests
{
    [Fact]
    public async Task TryExecuteAsync_DispatchesRegisteredHandlerWithContext()
    {
        var registry = new AIToolActionHandlerRegistry();
        using var cancellation = new CancellationTokenSource();
        registry.Register("echo", (arguments, context) =>
            Task.FromResult($"{arguments}:{context.CancellationToken.CanBeCanceled}"));

        string? result = await registry.TryExecuteAsync(
            "echo",
            "hello",
            new AIToolExecutionContext { CancellationToken = cancellation.Token });

        Assert.Equal("hello:True", result);
    }

    [Fact]
    public async Task TryExecuteAsync_ReturnsNullForUnknownAction()
    {
        var registry = new AIToolActionHandlerRegistry();

        string? result = await registry.TryExecuteAsync(
            "missing",
            "{}",
            new AIToolExecutionContext());

        Assert.Null(result);
    }

    [Fact]
    public void Register_ReplacesExistingHandlerByName()
    {
        var registry = new AIToolActionHandlerRegistry();
        registry.Register("action", (_, _) => Task.FromResult("first"));
        registry.Register("action", (_, _) => Task.FromResult("second"));

        Assert.Equal(["action"], registry.SnapshotNames());
    }
}

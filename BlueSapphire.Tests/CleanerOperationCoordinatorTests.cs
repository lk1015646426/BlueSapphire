using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public sealed class CleanerOperationCoordinatorTests
{
    [Fact]
    public void CoordinatorsWithSameName_AllowOnlyOneActiveOperation()
    {
        string gateName = $"Local\\BlueSapphire.Tests.Cleaner.{Guid.NewGuid():N}";
        var first = new CleanerOperationCoordinator(gateName);
        var second = new CleanerOperationCoordinator(gateName);

        Assert.True(first.TryAcquire(CleanerOperationKind.Scan, out CleanerOperationLease? firstLease));
        Assert.NotNull(firstLease);
        Assert.True(first.IsBusy);
        Assert.False(second.TryAcquire(CleanerOperationKind.Cleanup, out CleanerOperationLease? rejectedLease));
        Assert.Null(rejectedLease);

        firstLease.Dispose();
        firstLease.Dispose();

        Assert.False(first.IsBusy);
        Assert.True(second.TryAcquire(CleanerOperationKind.Cleanup, out CleanerOperationLease? secondLease));
        secondLease!.Dispose();
        Assert.False(second.IsBusy);
    }

    [Fact]
    public void StateChanged_ReportsAcquireAndRelease()
    {
        string gateName = $"Local\\BlueSapphire.Tests.Cleaner.{Guid.NewGuid():N}";
        var coordinator = new CleanerOperationCoordinator(gateName);
        var states = new List<bool>();
        coordinator.StateChanged += (_, _) => states.Add(coordinator.IsBusy);

        Assert.True(coordinator.TryAcquire(CleanerOperationKind.Restore, out CleanerOperationLease? lease));
        lease!.Dispose();

        Assert.Equal([true, false], states);
    }

    [Fact]
    public void ThrowingStateChangedSubscriber_DoesNotLeakOperationLease()
    {
        string gateName = $"Local\\BlueSapphire.Tests.Cleaner.{Guid.NewGuid():N}";
        var coordinator = new CleanerOperationCoordinator(gateName);
        coordinator.StateChanged += (_, _) => throw new InvalidOperationException("subscriber failure");

        bool acquired = coordinator.TryAcquire(
            CleanerOperationKind.Scan,
            out CleanerOperationLease? firstLease);

        Assert.True(acquired);
        Assert.NotNull(firstLease);
        firstLease.Dispose();
        Assert.False(coordinator.IsBusy);

        Assert.True(coordinator.TryAcquire(
            CleanerOperationKind.Cleanup,
            out CleanerOperationLease? secondLease));
        secondLease!.Dispose();
    }}

using System;
using System.Threading;

namespace BlueSapphire.Services;

public enum CleanerOperationKind
{
    Scan,
    Cleanup,
    AutomaticCleanup,
    Retry,
    Restore,
    Purge,
    AiScan,
    AiCleanup
}

/// <summary>
/// Coordinates cleaner operations inside the process and across BlueSapphire
/// processes in the current Windows session.
/// </summary>
public sealed class CleanerOperationCoordinator
{
    public const string DefaultGateName = @"Local\BlueSapphire.CleanerOperation.v1";

    private readonly object _sync = new();
    private readonly Semaphore _processGate;
    private Guid? _activeLeaseId;
    private CleanerOperationKind? _currentOperation;

    public CleanerOperationCoordinator()
        : this(DefaultGateName)
    {
    }

    public CleanerOperationCoordinator(string gateName)
    {
        if (string.IsNullOrWhiteSpace(gateName))
        {
            throw new ArgumentException("操作门禁名称不能为空。", nameof(gateName));
        }

        _processGate = new Semaphore(1, 1, gateName);
    }

    public event EventHandler? StateChanged;

    public bool IsBusy
    {
        get
        {
            lock (_sync)
            {
                return _activeLeaseId.HasValue;
            }
        }
    }

    public CleanerOperationKind? CurrentOperation
    {
        get
        {
            lock (_sync)
            {
                return _currentOperation;
            }
        }
    }

    public bool TryAcquire(CleanerOperationKind operation, out CleanerOperationLease? lease)
    {
        lease = null;
        Guid leaseId;

        lock (_sync)
        {
            if (_activeLeaseId.HasValue || !_processGate.WaitOne(0))
            {
                return false;
            }

            leaseId = Guid.NewGuid();
            _activeLeaseId = leaseId;
            _currentOperation = operation;
            lease = new CleanerOperationLease(this, leaseId, operation);
        }

        NotifyStateChanged();
        return true;
    }

    private void NotifyStateChanged()
    {
        EventHandler? handlers = StateChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // 状态通知是观察性回调，订阅者故障不能泄漏操作租约。
            }
        }
    }
    internal bool Owns(CleanerOperationLease lease)
    {
        lock (_sync)
        {
            return ReferenceEquals(lease.Coordinator, this) &&
                   _activeLeaseId == lease.LeaseId &&
                   !lease.IsDisposed;
        }
    }

    internal void Release(Guid leaseId)
    {
        bool released = false;
        lock (_sync)
        {
            if (_activeLeaseId != leaseId)
            {
                return;
            }

            _activeLeaseId = null;
            _currentOperation = null;
            _processGate.Release();
            released = true;
        }

        if (released)
        {
            NotifyStateChanged();
        }
    }
}

public sealed class CleanerOperationLease : IDisposable
{
    private int _disposed;

    internal CleanerOperationLease(
        CleanerOperationCoordinator coordinator,
        Guid leaseId,
        CleanerOperationKind operation)
    {
        Coordinator = coordinator;
        LeaseId = leaseId;
        Operation = operation;
    }

    internal CleanerOperationCoordinator Coordinator { get; }
    internal Guid LeaseId { get; }
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public CleanerOperationKind Operation { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Coordinator.Release(LeaseId);
        }
    }
}

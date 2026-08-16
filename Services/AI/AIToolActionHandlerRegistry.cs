using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services.AI;

public sealed class AIToolExecutionContext
{
    public Func<string, Task<bool>>? RequestConfirmation { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public delegate Task<string> AIToolActionHandler(
    string arguments,
    AIToolExecutionContext context);

/// <summary>
/// AI 工具动作的可注册分发器。
/// 它只负责把动作交给处理器，不负责实现领域业务，也不绕过权限策略。
/// </summary>
public sealed class AIToolActionHandlerRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AIToolActionHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string actionName, AIToolActionHandler handler)
    {
        if (string.IsNullOrWhiteSpace(actionName))
        {
            throw new ArgumentException("动作名称不能为空。", nameof(actionName));
        }

        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _handlers[actionName] = handler;
        }
    }

    public bool Contains(string actionName)
    {
        lock (_gate)
        {
            return _handlers.ContainsKey(actionName);
        }
    }

    public IReadOnlyList<string> SnapshotNames()
    {
        lock (_gate)
        {
            return _handlers.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public async Task<string?> TryExecuteAsync(
        string actionName,
        string arguments,
        AIToolExecutionContext context)
    {
        AIToolActionHandler? handler;
        lock (_gate)
        {
            _handlers.TryGetValue(actionName, out handler);
        }

        return handler == null
            ? null
            : await handler(arguments, context);
    }
}

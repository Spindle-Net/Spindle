using Spindle.Abstractions.Core;
using Spindle.Persistence.Conditions;

namespace Spindle.Persistence.InMemory.Stores;

/// <summary>
/// Stores durable condition metadata in memory.
/// </summary>
public sealed class InMemoryConditionWaitStore : IConditionWaitStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(FlowInstanceId FlowInstanceId, NodeId NodeId), ConditionWaitRecord> _waits = [];

    /// <inheritdoc />
    public ValueTask CreateAsync(
        ConditionWaitRecord wait,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var key = (wait.FlowInstanceId, wait.NodeId);
            if (_waits.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Condition wait '{wait.NodeId}' already exists for flow instance '{wait.FlowInstanceId}'.");
            }

            _waits.Add(key, wait with { });
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<ConditionWaitRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                _waits.TryGetValue((flowInstanceId, nodeId), out var wait)
                    ? wait with { }
                    : null);
        }
    }
}

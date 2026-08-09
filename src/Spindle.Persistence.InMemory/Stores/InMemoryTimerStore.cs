using Spindle.Abstractions.Core;
using Spindle.Persistence.Timers;

namespace Spindle.Persistence.InMemory.Stores;

public sealed class InMemoryTimerStore : ITimerStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(FlowInstanceId FlowInstanceId, NodeId NodeId), TimerRecord> _timers = [];

    public ValueTask CreateAsync(
        TimerRecord timer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _timers[(timer.FlowInstanceId, timer.NodeId)] = timer;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<TimerRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                _timers.TryGetValue((flowInstanceId, nodeId), out var timer)
                    ? timer
                    : null);
        }
    }

    public ValueTask<IReadOnlyList<TimerRecord>> GetDueAsync(
        DateTimeOffset dueAtOrBefore,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var timers = _timers.Values
                .Where(timer => timer.FiredAt is null && timer.DueAt <= dueAtOrBefore)
                .OrderBy(timer => timer.DueAt)
                .Take(maxCount)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<TimerRecord>>(timers);
        }
    }

    public ValueTask MarkFiredAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        DateTimeOffset firedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var key = (flowInstanceId, nodeId);
            if (_timers.TryGetValue(key, out var timer))
            {
                _timers[key] = timer with { FiredAt = firedAt };
            }
        }

        return ValueTask.CompletedTask;
    }
}

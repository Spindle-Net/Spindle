using Spindle.Abstractions.Core;
using Spindle.Persistence.Signals;

namespace Spindle.Persistence.InMemory.Stores;

public sealed class InMemorySignalStore : ISignalStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(FlowInstanceId FlowInstanceId, StepId StepId), SignalWaitRecord> _waits = [];
    private readonly List<SignalRecord> _signals = [];

    public ValueTask CreateWaitAsync(
        SignalWaitRecord wait,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _waits[(wait.FlowInstanceId, wait.StepId)] = wait;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<SignalWaitRecord>> GetOpenWaitsAsync(
        SignalName signalName,
        CorrelationKey? correlationKey = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var waits = _waits.Values
                .Where(wait =>
                    wait.CompletedAt is null &&
                    wait.SignalName == signalName &&
                    (correlationKey is null || wait.CorrelationKey == correlationKey))
                .OrderBy(wait => wait.CreatedAt)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<SignalWaitRecord>>(waits);
        }
    }

    public ValueTask MarkWaitCompletedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var key = (flowInstanceId, stepId);
            if (_waits.TryGetValue(key, out var wait))
            {
                _waits[key] = wait with { CompletedAt = completedAt };
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AppendSignalAsync(
        SignalRecord signal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _signals.Add(InMemoryRecordCopies.Copy(signal));
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<SignalRecord> GetSignals()
    {
        lock (_gate)
        {
            return _signals.Select(InMemoryRecordCopies.Copy).ToArray();
        }
    }
}

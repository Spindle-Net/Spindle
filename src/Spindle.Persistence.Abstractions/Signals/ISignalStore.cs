using Spindle.Abstractions.Core;

namespace Spindle.Persistence.Signals;

public interface ISignalStore
{
    ValueTask CreateWaitAsync(
        SignalWaitRecord wait,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SignalWaitRecord>> GetOpenWaitsAsync(
        SignalName signalName,
        CorrelationKey? correlationKey = null,
        CancellationToken cancellationToken = default);

    ValueTask MarkWaitCompletedAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    ValueTask AppendSignalAsync(
        SignalRecord signal,
        CancellationToken cancellationToken = default);
}

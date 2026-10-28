using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.FlowInstances;

public interface IFlowInstanceStore
{
    /// <summary>Reopens a recoverable flow and clears its terminal error and completion time.</summary>
    ValueTask<bool> TryReopenAsync(FlowInstanceId instanceId, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    ValueTask CreateAsync(
        FlowInstanceRecord instance,
        CancellationToken cancellationToken = default);

    ValueTask<FlowInstanceRecord?> GetAsync(
        FlowInstanceId instanceId,
        CancellationToken cancellationToken = default);

    ValueTask<FlowInstanceRecord?> GetByIdempotencyKeyAsync(
        FlowName flowName,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<FlowInstanceRecord>> GetRunnableAsync(
        int maxCount,
        CancellationToken cancellationToken = default);

    ValueTask UpdateStatusAsync(
        FlowInstanceId instanceId,
        FlowInstanceStatus status,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkCompletedAsync(
        FlowInstanceId instanceId,
        SerializedPayload result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    ValueTask MarkFailedAsync(
        FlowInstanceId instanceId,
        string error,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default);
}

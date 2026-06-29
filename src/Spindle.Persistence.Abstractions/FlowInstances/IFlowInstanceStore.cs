using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.FlowInstances;

public interface IFlowInstanceStore
{
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

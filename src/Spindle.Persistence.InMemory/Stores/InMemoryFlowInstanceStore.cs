using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.FlowInstances;

namespace Spindle.Persistence.InMemory.Stores;

public sealed class InMemoryFlowInstanceStore : IFlowInstanceStore
{
    private readonly object _gate = new();
    private readonly Dictionary<FlowInstanceId, FlowInstanceRecord> _instances = [];

    public ValueTask CreateAsync(
        FlowInstanceRecord instance,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_instances.ContainsKey(instance.InstanceId))
            {
                throw new InvalidOperationException(
                    $"Flow instance '{instance.InstanceId}' already exists.");
            }

            if (instance.IdempotencyKey is { } idempotencyKey &&
                _instances.Values.Any(existing =>
                    existing.FlowName == instance.FlowName &&
                    string.Equals(existing.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Flow '{instance.FlowName}' already has an instance for idempotency key '{idempotencyKey}'.");
            }

            _instances.Add(instance.InstanceId, InMemoryRecordCopies.Copy(instance));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<FlowInstanceRecord?> GetAsync(
        FlowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                _instances.TryGetValue(instanceId, out var instance)
                    ? InMemoryRecordCopies.Copy(instance)
                    : null);
        }
    }

    public ValueTask<FlowInstanceRecord?> GetByIdempotencyKeyAsync(
        FlowName flowName,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var instance = _instances.Values.FirstOrDefault(existing =>
                existing.FlowName == flowName &&
                string.Equals(existing.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

            return ValueTask.FromResult(
                instance is null ? null : InMemoryRecordCopies.Copy(instance));
        }
    }

    public ValueTask<IReadOnlyList<FlowInstanceRecord>> GetRunnableAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var instances = _instances.Values
                .Where(instance => instance.Status is not (
                    FlowInstanceStatus.Completed or
                    FlowInstanceStatus.Failed or
                    FlowInstanceStatus.Cancelled or
                    FlowInstanceStatus.TimedOut))
                .OrderBy(instance => instance.UpdatedAt)
                .ThenBy(instance => instance.CreatedAt)
                .ThenBy(instance => instance.InstanceId.Value, StringComparer.Ordinal)
                .Take(maxCount)
                .Select(InMemoryRecordCopies.Copy)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<FlowInstanceRecord>>(instances);
        }
    }

    public ValueTask UpdateStatusAsync(
        FlowInstanceId instanceId,
        FlowInstanceStatus status,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var instance = GetRequired(instanceId);
            _instances[instanceId] = instance with
            {
                Status = status,
                UpdatedAt = updatedAt
            };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkCompletedAsync(
        FlowInstanceId instanceId,
        SerializedPayload result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var instance = GetRequired(instanceId);
            _instances[instanceId] = instance with
            {
                Status = FlowInstanceStatus.Completed,
                Result = InMemoryRecordCopies.Copy(result),
                Error = null,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkFailedAsync(
        FlowInstanceId instanceId,
        string error,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var instance = GetRequired(instanceId);
            _instances[instanceId] = instance with
            {
                Status = FlowInstanceStatus.Failed,
                Error = error,
                CompletedAt = failedAt,
                UpdatedAt = failedAt
            };
        }

        return ValueTask.CompletedTask;
    }

    private FlowInstanceRecord GetRequired(FlowInstanceId instanceId)
    {
        return _instances.TryGetValue(instanceId, out var instance)
            ? instance
            : throw new InvalidOperationException($"Flow instance '{instanceId}' does not exist.");
    }
}

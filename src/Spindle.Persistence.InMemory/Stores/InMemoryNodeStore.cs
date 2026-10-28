using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence.Nodes;

namespace Spindle.Persistence.InMemory.Stores;

public sealed class InMemoryNodeStore : INodeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(FlowInstanceId FlowInstanceId, NodeId NodeId), NodeInstanceRecord> _nodes = [];
    private readonly List<StepAttemptRecord> _attempts = [];

    public ValueTask CreateAsync(
        NodeInstanceRecord node,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var key = (node.FlowInstanceId, node.NodeId);

            if (_nodes.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Step '{node.NodeId}' already exists for flow instance '{node.FlowInstanceId}'.");
            }

            _nodes.Add(key, InMemoryRecordCopies.Copy(node));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CreateManyAsync(
        IReadOnlyList<NodeInstanceRecord> nodes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(nodes);

        lock (_gate)
        {
            var keys = new HashSet<(FlowInstanceId FlowInstanceId, NodeId NodeId)>();

            foreach (var node in nodes)
            {
                var key = (node.FlowInstanceId, node.NodeId);

                if (!keys.Add(key) || _nodes.ContainsKey(key))
                {
                    throw new InvalidOperationException(
                        $"Step '{node.NodeId}' already exists for flow instance '{node.FlowInstanceId}'.");
                }
            }

            foreach (var node in nodes)
            {
                _nodes.Add((node.FlowInstanceId, node.NodeId), InMemoryRecordCopies.Copy(node));
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<NodeInstanceRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                _nodes.TryGetValue((flowInstanceId, nodeId), out var node)
                    ? InMemoryRecordCopies.Copy(node)
                    : null);
        }
    }

    public ValueTask<IReadOnlyList<NodeInstanceRecord>> GetManyAsync(
        FlowInstanceId flowInstanceId,
        IReadOnlyList<NodeId> nodeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(nodeIds);

        lock (_gate)
        {
            var nodes = nodeIds
                .Distinct()
                .Select(nodeId => _nodes.TryGetValue((flowInstanceId, nodeId), out var node)
                    ? InMemoryRecordCopies.Copy(node)
                    : null)
                .Where(node => node is not null)
                .Cast<NodeInstanceRecord>()
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<NodeInstanceRecord>>(nodes);
        }
    }

    public ValueTask<IReadOnlyList<NodeInstanceRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var nodes = _nodes.Values
                .Where(node => node.FlowInstanceId == flowInstanceId)
                .OrderBy(node => node.CreatedAt)
                .ThenBy(node => node.NodeId.Value, StringComparer.Ordinal)
                .Select(InMemoryRecordCopies.Copy)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<NodeInstanceRecord>>(nodes);
        }
    }

    public ValueTask<IReadOnlyList<StepAttemptRecord>> GetAttemptsAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var attempts = _attempts
                .Where(attempt => attempt.FlowInstanceId == flowInstanceId && attempt.NodeId == nodeId)
                .OrderBy(attempt => attempt.Attempt)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<StepAttemptRecord>>(attempts);
        }
    }

    public ValueTask<IReadOnlyList<NodeInstanceRecord>> GetReadyNodesAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var nodes = _nodes.Values
                .Where(node => node.Status == NodeStatus.Ready)
                .OrderBy(node => node.CreatedAt)
                .ThenBy(node => node.NodeId.Value, StringComparer.Ordinal)
                .Take(maxCount)
                .Select(InMemoryRecordCopies.Copy)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<NodeInstanceRecord>>(nodes);
        }
    }

    public ValueTask MarkReadyAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = GetRequired(flowInstanceId, nodeId);

            if (node.Status is NodeStatus.Completed
                or NodeStatus.Failed
                or NodeStatus.Cancelled
                or NodeStatus.TimedOut
                or NodeStatus.Skipped)
            {
                return ValueTask.CompletedTask;
            }

            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.Ready,
                UpdatedAt = updatedAt
            };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkRunningAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        StepAttemptId attemptId,
        string workerId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = GetRequired(flowInstanceId, nodeId);
            var nextAttempt = node.Attempt + 1;

            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.Running,
                Attempt = nextAttempt,
                StartedAt = startedAt,
                UpdatedAt = startedAt
            };

            _attempts.Add(new StepAttemptRecord
            {
                FlowInstanceId = flowInstanceId,
                NodeId = nodeId,
                AttemptId = attemptId,
                Attempt = nextAttempt,
                WorkerId = workerId,
                Status = NodeStatus.Running,
                StartedAt = startedAt
            });
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkWaitingAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = GetRequired(flowInstanceId, nodeId);
            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.Waiting,
                UpdatedAt = updatedAt
            };

            CompleteLatestAttempt(flowInstanceId, nodeId, attempt, NodeStatus.Waiting, updatedAt, null);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkTimedOutAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        string error,
        DateTimeOffset timedOutAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = GetRequired(flowInstanceId, nodeId);
            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.TimedOut,
                Error = error,
                CompletedAt = timedOutAt,
                UpdatedAt = timedOutAt
            };

            CompleteLatestAttempt(flowInstanceId, nodeId, attempt, NodeStatus.TimedOut, timedOutAt, error);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkCompletedAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        SerializedPayload? result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = GetRequired(flowInstanceId, nodeId);
            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.Completed,
                Result = InMemoryRecordCopies.Copy(result),
                Error = null,
                RetryAt = null,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            };

            CompleteLatestAttempt(flowInstanceId, nodeId, attempt, NodeStatus.Completed, completedAt, null);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkFailedAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = GetRequired(flowInstanceId, nodeId);
            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.Failed,
                Error = error,
                RetryAt = retryAt,
                CompletedAt = failedAt,
                UpdatedAt = failedAt
            };

            CompleteLatestAttempt(flowInstanceId, nodeId, attempt, NodeStatus.Failed, failedAt, error);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TryScheduleRetryAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        NodeStatus attemptOutcome,
        string error,
        DateTimeOffset retryAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = GetRequired(flowInstanceId, nodeId);
            if (node.Status != NodeStatus.Running || node.Attempt != attempt)
            {
                return ValueTask.FromResult(false);
            }

            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.Waiting,
                Error = error,
                RetryAt = retryAt,
                UpdatedAt = updatedAt
            };

            CompleteLatestAttempt(flowInstanceId, nodeId, attempt, attemptOutcome, updatedAt, error);
        }

        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> TryMarkRetryReadyAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        DateTimeOffset retryAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_nodes.TryGetValue((flowInstanceId, nodeId), out var node) ||
                node.Status != NodeStatus.Waiting ||
                node.RetryAt != retryAt)
            {
                return ValueTask.FromResult(false);
            }

            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.Ready,
                RetryAt = null,
                UpdatedAt = updatedAt
            };
        }

        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> TryInterruptAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attempt,
        DateTimeOffset interruptedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = GetRequired(flowInstanceId, nodeId);
            if (node.Status != NodeStatus.Running || node.Attempt != attempt)
            {
                return ValueTask.FromResult(false);
            }

            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.Ready,
                RetryAttemptBaseline = node.RetryAttemptBaseline + 1,
                UpdatedAt = interruptedAt
            };

            CompleteLatestAttempt(flowInstanceId, nodeId, attempt, NodeStatus.Cancelled, interruptedAt, null);
        }

        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> TryScheduleRecoveryAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        NodeStatus expectedStatus,
        int expectedAttempt,
        DateTimeOffset retryAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = GetRequired(flowInstanceId, nodeId);
            if (node.Status != expectedStatus || node.Attempt != expectedAttempt)
            {
                return ValueTask.FromResult(false);
            }

            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.Waiting,
                RetryAt = retryAt,
                RetryAttemptBaseline = node.Attempt,
                CompletedAt = null,
                UpdatedAt = updatedAt
            };
        }

        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> TryReopenBarrierAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = GetRequired(flowInstanceId, nodeId);
            if (node.Kind is not (NodeKind.WaitAll or NodeKind.WaitAny) ||
                node.Status != NodeStatus.Failed)
            {
                return ValueTask.FromResult(false);
            }

            _nodes[(flowInstanceId, nodeId)] = node with
            {
                Status = NodeStatus.Waiting,
                Result = null,
                Error = null,
                CompletedAt = null,
                UpdatedAt = updatedAt
            };
        }

        return ValueTask.FromResult(true);
    }

    public ValueTask MarkDependentsReadyAsync(
        FlowInstanceId flowInstanceId,
        List<NodeId>? updatedNodes,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var nodes = _nodes.Values
                .Where(node => node.FlowInstanceId == flowInstanceId)
                .OrderBy(node => node.CreatedAt)
                .ThenBy(node => node.NodeId.Value, StringComparer.Ordinal)
                .ToArray();

            var completed = nodes
                .Where(node => node.Status == NodeStatus.Completed)
                .Select(node => node.NodeId)
                .ToHashSet();

            foreach (var node in nodes)
            {
                if (node.Kind is not (NodeKind.Step or NodeKind.ConditionWait) ||
                    node.Status != NodeStatus.Pending)
                {
                    continue;
                }

                if (node.Dependencies.Count == 0 ||
                    node.Dependencies.All(completed.Contains))
                {
                    _nodes[(flowInstanceId, node.NodeId)] = node with
                    {
                        Status = NodeStatus.Ready,
                        UpdatedAt = updatedAt
                    };
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private NodeInstanceRecord GetRequired(FlowInstanceId flowInstanceId, NodeId nodeId)
    {
        return _nodes.TryGetValue((flowInstanceId, nodeId), out var node)
            ? node
            : throw new InvalidOperationException(
                $"Step '{nodeId}' does not exist for flow instance '{flowInstanceId}'.");
    }

    private void CompleteLatestAttempt(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        int attemptIx,
        NodeStatus status,
        DateTimeOffset completedAt,
        string? error)
    {
        var attemptIndex = _attempts.FindLastIndex(attempt =>
            attempt.FlowInstanceId == flowInstanceId &&
            attempt.NodeId == nodeId &&
            attempt.CompletedAt is null &&
            attempt.Attempt == attemptIx);

        if (attemptIndex < 0)
        {
            return;
        }

        _attempts[attemptIndex] = _attempts[attemptIndex] with
        {
            Status = status,
            CompletedAt = completedAt,
            Error = error
        };
    }
}

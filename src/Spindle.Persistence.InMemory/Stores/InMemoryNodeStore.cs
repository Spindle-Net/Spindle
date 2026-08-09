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

            if (node.Status is NodeStatus.Completed or NodeStatus.Failed or NodeStatus.Cancelled)
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
                if (node.Kind != NodeKind.Step || node.Status != NodeStatus.Pending)
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

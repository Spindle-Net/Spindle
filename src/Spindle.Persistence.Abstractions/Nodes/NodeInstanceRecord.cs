using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle.Persistence.Nodes;

public sealed record NodeInstanceRecord
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public required NodeId NodeId { get; init; }

    public required string Name { get; init; }

    public required NodeKind Kind { get; init; }

    public required NodeStatus Status { get; init; }

    public StepHandlerId? HandlerId { get; init; }

    public QueueName? Queue { get; init; }

    public StepDispatchMode DispatchMode { get; init; }

    public DependencySatisfactionMode DependencyMode { get; init; }
        = DependencySatisfactionMode.AllSucceeded;

    public IReadOnlyList<NodeId> Dependencies { get; init; } = [];

    public SerializedPayload? Input { get; init; }

    public SerializedPayload? Result { get; init; }

    public string? Error { get; init; }

    public int Attempt { get; init; }

    public DateTimeOffset? RetryAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

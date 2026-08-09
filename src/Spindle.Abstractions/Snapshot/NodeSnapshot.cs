using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle.Abstractions.Snapshot;

public sealed record NodeSnapshot
{
    public required NodeId NodeId { get; init; }

    public required string Name { get; init; }

    public required NodeKind Kind { get; init; }

    public required NodeStatus Status { get; init; }

    public StepHandlerId? HandlerId { get; init; }

    public QueueName? Queue { get; init; }

    public int Attempt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? LastError { get; init; }
}
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.Nodes;

public sealed record StepAttemptRecord
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public required NodeId NodeId { get; init; }

    public required StepAttemptId AttemptId { get; init; }

    public required int Attempt { get; init; }

    public required string WorkerId { get; init; }

    public required NodeStatus Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Error { get; init; }
}

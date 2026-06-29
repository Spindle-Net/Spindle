using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Snapshot;

public sealed record FlowInstanceSnapshot
{
    public required FlowInstanceId InstanceId { get; init; }

    public required FlowName FlowName { get; init; }

    public required FlowVersion FlowVersion { get; init; }

    public required FlowInstanceStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public IReadOnlyList<StepSnapshot> Steps { get; init; }
        = [];
}
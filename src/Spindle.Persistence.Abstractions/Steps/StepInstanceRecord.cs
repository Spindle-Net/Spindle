using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;

namespace Spindle.Persistence.Steps;

public sealed record StepInstanceRecord
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public required StepId StepId { get; init; }

    public required string Name { get; init; }

    public required StepKind Kind { get; init; }

    public required StepStatus Status { get; init; }

    public StepHandlerId? HandlerId { get; init; }

    public QueueName? Queue { get; init; }

    public StepDispatchMode DispatchMode { get; init; }

    public IReadOnlyList<StepId> Dependencies { get; init; } = [];

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

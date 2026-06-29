using Spindle.Abstractions.Core;
using Spindle.Abstractions.Steps;

namespace Spindle.Abstractions.Snapshot;

public sealed record StepSnapshot
{
    public required StepId StepId { get; init; }

    public required string Name { get; init; }

    public required StepKind Kind { get; init; }

    public required StepStatus Status { get; init; }

    public StepHandlerId? HandlerId { get; init; }

    public QueueName? Queue { get; init; }

    public int Attempt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? LastError { get; init; }
}
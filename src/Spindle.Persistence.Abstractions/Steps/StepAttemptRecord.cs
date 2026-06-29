using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.Steps;

public sealed record StepAttemptRecord
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public required StepId StepId { get; init; }

    public required StepAttemptId AttemptId { get; init; }

    public required int Attempt { get; init; }

    public required string WorkerId { get; init; }

    public required StepStatus Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Error { get; init; }
}

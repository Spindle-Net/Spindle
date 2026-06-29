using Spindle.Abstractions.Core;

namespace Spindle.Persistence.Timers;

public sealed record TimerRecord
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public required StepId StepId { get; init; }

    public required DateTimeOffset DueAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? FiredAt { get; init; }
}

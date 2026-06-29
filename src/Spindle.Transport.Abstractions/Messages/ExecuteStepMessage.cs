using Spindle.Abstractions.Core;

namespace Spindle.Transport.Messages;

public sealed record ExecuteStepMessage
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public required StepId StepId { get; init; }

    public required StepAttemptId AttemptId { get; init; }

    public required int Attempt { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }
}

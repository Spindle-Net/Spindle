using Spindle.Abstractions.Core;

namespace Spindle.Transport.Messages;

public sealed record StepFailedMessage
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public required StepId StepId { get; init; }

    public required StepAttemptId AttemptId { get; init; }

    public required string Error { get; init; }

    public DateTimeOffset? RetryAt { get; init; }

    public required DateTimeOffset FailedAt { get; init; }
}

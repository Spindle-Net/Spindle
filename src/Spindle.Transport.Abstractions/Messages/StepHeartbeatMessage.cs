using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Transport.Messages;

public sealed record StepHeartbeatMessage
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public required StepId StepId { get; init; }

    public required StepAttemptId AttemptId { get; init; }

    public SerializedPayload? Progress { get; init; }

    public required DateTimeOffset HeartbeatAt { get; init; }
}

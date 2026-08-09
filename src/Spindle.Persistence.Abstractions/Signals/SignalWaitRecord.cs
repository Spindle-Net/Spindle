using Spindle.Abstractions.Core;

namespace Spindle.Persistence.Signals;

public sealed record SignalWaitRecord
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public required NodeId NodeId { get; init; }

    public required SignalName SignalName { get; init; }

    public CorrelationKey CorrelationKey { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

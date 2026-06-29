using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.Signals;

public sealed record SignalRecord
{
    public required SignalName SignalName { get; init; }

    public CorrelationKey? CorrelationKey { get; init; }

    public FlowInstanceId? FlowInstanceId { get; init; }

    public required SerializedPayload Payload { get; init; }

    public required DateTimeOffset RaisedAt { get; init; }
}

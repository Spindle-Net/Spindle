using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.History;

public sealed record ExecutionHistoryRecord
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public StepId? StepId { get; init; }

    public required string EventType { get; init; }

    public SerializedPayload? Payload { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.EFCore.Entities;

internal class ExecutionHistoryEntity
{
    public long Id { get; set; }

    public required string FlowInstanceId { get; init; }

    public string? StepId { get; init; }

    public required string EventType { get; init; }

    public required SerializedPayload? Payload { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}

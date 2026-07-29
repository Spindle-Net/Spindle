using Microsoft.EntityFrameworkCore;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(StepId))]
internal class SignalWaitEntity
{
    public required string FlowInstanceId { get; init; }

    public required string StepId { get; init; }

    public required string SignalName { get; init; }

    public string? CorrelationKey { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }
}

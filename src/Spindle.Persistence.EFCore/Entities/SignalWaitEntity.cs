using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(StepId))]
[Index(nameof(SignalName), nameof(CorrelationKey), nameof(CompletedAt))]
internal class SignalWaitEntity
{
    [MaxLength(255)]
    public required string FlowInstanceId { get; init; }

    [MaxLength(255)]
    public required string StepId { get; init; }

    [MaxLength(255)]
    public required string SignalName { get; init; }

    [MaxLength(255)]
    public string? CorrelationKey { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }
}

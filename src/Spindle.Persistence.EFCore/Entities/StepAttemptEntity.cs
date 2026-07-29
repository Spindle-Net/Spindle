using Spindle.Abstractions.Snapshot;
using System.ComponentModel.DataAnnotations;

namespace Spindle.Persistence.EFCore.Entities;

internal class StepAttemptEntity
{
    public required string FlowInstanceId { get; init; }

    public required string StepId { get; init; }

    public StepInstanceEntity? Step { get; set; }
    [Key]
    public required string AttemptId { get; init; }

    public required int Attempt { get; init; }

    public required string WorkerId { get; init; }

    public required StepStatus Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Error { get; init; }
}

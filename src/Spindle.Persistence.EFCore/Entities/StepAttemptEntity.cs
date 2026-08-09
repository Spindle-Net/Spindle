using Spindle.Abstractions.Snapshot;
using System.ComponentModel.DataAnnotations;

namespace Spindle.Persistence.EFCore.Entities;

internal class StepAttemptEntity
{
    public required string FlowInstanceId { get; init; }

    public required string NodeId { get; init; }

    [Key]
    [MaxLength(255)]
    public required string AttemptId { get; init; }

    public required int Attempt { get; init; }

    public required string WorkerId { get; init; }

    public required NodeStatus Status { get; set; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Error { get; set; }
}

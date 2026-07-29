using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using System.ComponentModel.DataAnnotations.Schema;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(StepId))]
internal class StepInstanceEntity
{
    public required string FlowInstanceId { get; init; }
    [ForeignKey(nameof(FlowInstanceId))]
    public FlowInstanceEntity? FlowInstance { get; init; }

    public required string StepId { get; init; }

    public required string Name { get; init; }

    public required StepKind Kind { get; init; }

    public required StepStatus Status { get; init; }

    public string? HandlerId { get; init; }

    public string? Queue { get; init; }

    public StepDispatchMode DispatchMode { get; init; }

    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public SerializedPayload? Input { get; init; }

    public SerializedPayload? Result { get; init; }

    public string? Error { get; init; }

    public int Attempt { get; init; }

    public DateTimeOffset? RetryAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public ICollection<SignalWaitEntity>? SignalWaits { get; set; }
    public ICollection<StepLeaseEntity>? Leases { get; set; }
    public ICollection<StepAttemptEntity>? Attempts { get; set; }
    public ICollection<TimerEntity>? Timers { get; set; }
}

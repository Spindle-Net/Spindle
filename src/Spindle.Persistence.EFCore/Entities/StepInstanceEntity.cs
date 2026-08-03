using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using System.ComponentModel.DataAnnotations;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(StepId))]
[Index(nameof(Status), nameof(CreatedAt))]
internal class StepInstanceEntity
{
    [MaxLength(255)]
    public required string FlowInstanceId { get; init; }

    [MaxLength(255)]
    public required string StepId { get; init; }

    public required string Name { get; init; }

    public required StepKind Kind { get; init; }

    public required StepStatus Status { get; set; }

    public string? HandlerId { get; init; }

    public string? Queue { get; init; }

    public StepDispatchMode DispatchMode { get; init; }

    /// <summary>
    /// The ones the step need in order to continue
    /// </summary>
    public List<StepDependencyEntity> Dependencies { get; init; } = [];

    /// <summary>
    /// Who needs me
    /// </summary>
    public List<StepDependencyEntity> Dependents { get; init; } = [];

    public SerializedPayload? Input { get; init; }

    public SerializedPayload? Result { get; set; }

    public string? Error { get; set; }

    public int Attempt { get; set; }

    public DateTimeOffset? RetryAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

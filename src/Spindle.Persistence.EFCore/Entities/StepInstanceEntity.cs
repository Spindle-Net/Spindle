using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(StepId))]
internal class StepInstanceEntity
{
    public required string FlowInstanceId { get; init; }
    public required string StepId { get; init; }

    public required string Name { get; init; }

    public required StepKind Kind { get; init; }

    public required StepStatus Status { get; set; }

    public string? HandlerId { get; init; }

    public string? Queue { get; init; }

    public StepDispatchMode DispatchMode { get; init; }

    public IReadOnlyList<string> Dependencies { get; init; } = [];

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

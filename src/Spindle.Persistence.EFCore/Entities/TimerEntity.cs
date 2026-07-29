using Microsoft.EntityFrameworkCore;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(StepId))]
internal class TimerEntity
{
    public required string FlowInstanceId { get; init; }

    public required string StepId { get; init; }

    public required DateTimeOffset DueAt { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? FiredAt { get; set; }
}

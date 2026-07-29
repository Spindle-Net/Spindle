using Microsoft.EntityFrameworkCore;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(StepId))]
internal class StepLeaseEntity
{
    public required string FlowInstanceId { get; init; }

    public required string StepId { get; init; }

    public StepInstanceEntity? Step { get; set; }

    public required string Owner { get; set; }

    public required DateTimeOffset AcquiredAt { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }
}

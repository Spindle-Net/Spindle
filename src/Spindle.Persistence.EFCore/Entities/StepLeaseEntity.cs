using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(StepId))]
internal class StepLeaseEntity
{
    [MaxLength(255)]
    public required string FlowInstanceId { get; init; }

    [MaxLength(255)]
    public required string StepId { get; init; }

    public required string Owner { get; set; }

    public required DateTimeOffset AcquiredAt { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }
}

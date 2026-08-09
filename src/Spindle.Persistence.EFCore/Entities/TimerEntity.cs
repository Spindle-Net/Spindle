using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(NodeId))]
[Index(nameof(FiredAt), nameof(DueAt))]
internal class TimerEntity
{
    [MaxLength(255)]
    public required string FlowInstanceId { get; init; }

    [MaxLength(255)]
    public required string NodeId { get; init; }

    public required DateTimeOffset DueAt { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? FiredAt { get; set; }
}

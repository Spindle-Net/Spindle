using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(NodeId))]
[Index(nameof(ExpiresAt))]
internal sealed class ConditionWaitEntity
{
    [MaxLength(255)]
    public required string FlowInstanceId { get; init; }

    [MaxLength(255)]
    public required string NodeId { get; init; }

    public required long PollingIntervalTicks { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

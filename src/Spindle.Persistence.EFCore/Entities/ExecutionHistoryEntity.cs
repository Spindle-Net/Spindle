using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using System.ComponentModel.DataAnnotations.Schema;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowInstanceId), nameof(StepId))]
internal class ExecutionHistoryEntity
{
    public required string FlowInstanceId { get; init; }
    [ForeignKey(nameof(FlowInstanceId))]
    public FlowInstanceEntity? FlowInstance { get; set; }

    public StepId? StepId { get; init; }

    public required string EventType { get; init; }

    public required SerializedPayload? Payload { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}

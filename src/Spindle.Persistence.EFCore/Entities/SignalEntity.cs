using Spindle.Abstractions.Snapshot;
using System.ComponentModel.DataAnnotations.Schema;

namespace Spindle.Persistence.EFCore.Entities;

internal class SignalEntity
{
    public int Id { get; set; }
    
    public required string SignalName { get; init; }

    public string? CorrelationKey { get; init; }

    public string? FlowInstanceId { get; init; }

    [ForeignKey(nameof(FlowInstanceId))]
    public FlowInstanceEntity? FlowInstance { get; set; }

    public required SerializedPayload Payload { get; init; }

    public required DateTimeOffset RaisedAt { get; init; }
}

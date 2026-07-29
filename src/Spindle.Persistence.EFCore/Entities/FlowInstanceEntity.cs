using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(InstanceId))]
internal class FlowInstanceEntity
{

    public required string InstanceId { get; init; }

    public required string FlowName { get; init; }

    public required string FlowVersion { get; init; }

    public required string DefinitionHash { get; init; }

    public required FlowInstanceStatus Status { get; set; }

    public required SerializedPayload Input { get; init; }

    public SerializedPayload? Result { get; set; }

    public string? Error { get; set; }

    public string? CorrelationKey { get; init; }

    public string? IdempotencyKey { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

}

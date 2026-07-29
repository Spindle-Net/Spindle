using Microsoft.EntityFrameworkCore;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.EFCore.Entities;

[PrimaryKey(nameof(FlowName), nameof(FlowVersion))]
internal class FlowInstanceEntity
{

    public required string InstanceId { get; init; }

    public required string FlowName { get; init; }

    public required string FlowVersion { get; init; }

    public FlowDefinitionEntity? FlowDefinition { get; set; }

    public required string DefinitionHash { get; init; }

    public required FlowInstanceStatus Status { get; init; }

    public required SerializedPayload Input { get; init; }

    public SerializedPayload? Result { get; init; }

    public string? Error { get; init; }

    public string? CorrelationKey { get; init; }

    public string? IdempotencyKey { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

}

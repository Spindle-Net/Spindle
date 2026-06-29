using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Persistence.FlowInstances;

public sealed record FlowInstanceRecord
{
    public required FlowInstanceId InstanceId { get; init; }

    public required FlowName FlowName { get; init; }

    public required FlowVersion FlowVersion { get; init; }

    public required string DefinitionHash { get; init; }

    public required FlowInstanceStatus Status { get; init; }

    public required SerializedPayload Input { get; init; }

    public SerializedPayload? Result { get; init; }

    public string? Error { get; init; }

    public CorrelationKey? CorrelationKey { get; init; }

    public string? IdempotencyKey { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

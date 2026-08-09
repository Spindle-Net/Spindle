using Spindle.Abstractions.Core;

namespace Spindle.Persistence.Leases;

public sealed record StepLeaseRecord
{
    public required FlowInstanceId FlowInstanceId { get; init; }

    public required NodeId NodeId { get; init; }

    public required string Owner { get; init; }

    public required DateTimeOffset AcquiredAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

using Spindle.Abstractions.Core;

namespace Spindle.Persistence.Conditions;

/// <summary>
/// Describes the durable scheduling metadata for a condition wait.
/// </summary>
public sealed record ConditionWaitRecord
{
    /// <summary>Gets the owning flow instance.</summary>
    public required FlowInstanceId FlowInstanceId { get; init; }

    /// <summary>Gets the condition node identifier.</summary>
    public required NodeId NodeId { get; init; }

    /// <summary>Gets the delay between unsuccessful checks.</summary>
    public required TimeSpan PollingInterval { get; init; }

    /// <summary>Gets the optional absolute timeout deadline.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets the time at which the condition was first declared.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

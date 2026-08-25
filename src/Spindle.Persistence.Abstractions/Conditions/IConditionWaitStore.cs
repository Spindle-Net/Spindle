using Spindle.Abstractions.Core;

namespace Spindle.Persistence.Conditions;

/// <summary>
/// Persists scheduling metadata for durable condition waits.
/// </summary>
public interface IConditionWaitStore
{
    /// <summary>Creates condition scheduling metadata.</summary>
    ValueTask CreateAsync(
        ConditionWaitRecord wait,
        CancellationToken cancellationToken = default);

    /// <summary>Gets condition scheduling metadata.</summary>
    ValueTask<ConditionWaitRecord?> GetAsync(
        FlowInstanceId flowInstanceId,
        NodeId nodeId,
        CancellationToken cancellationToken = default);
}

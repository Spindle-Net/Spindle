using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Represents a durable wait that polls an asynchronous condition until it succeeds.
/// </summary>
public abstract class ConditionNode : WaitNode<Unit>
{
    /// <summary>
    /// Gets the delay between unsuccessful condition checks.
    /// </summary>
    public abstract TimeSpan PollingInterval { get; }

    /// <summary>
    /// Gets the optional timeout measured from the node's first declaration.
    /// </summary>
    public abstract TimeSpan? Timeout { get; }

    /// <summary>
    /// Configures the maximum duration in which the condition may become true.
    /// </summary>
    public abstract ConditionNode WithTimeout(TimeSpan timeout);
}

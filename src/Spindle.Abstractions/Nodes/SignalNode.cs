using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Represents a durable wait for a correlated signal.
/// </summary>
/// <typeparam name="TSignal">The signal payload type.</typeparam>
public abstract class SignalNode<TSignal> : WaitNode<TSignal?>
{
    public abstract SignalName SignalName { get; }

    public abstract CorrelationKey CorrelationKey { get; }
}

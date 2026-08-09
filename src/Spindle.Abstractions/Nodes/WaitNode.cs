namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Represents an engine-managed node that suspends flow progress until it completes.
/// </summary>
/// <typeparam name="TResult">The wait result type.</typeparam>
public abstract class WaitNode<TResult> : Node<TResult>
{
}

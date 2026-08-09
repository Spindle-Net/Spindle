using System.Runtime.CompilerServices;

namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Represents a durable flow node that produces a result.
/// </summary>
/// <typeparam name="T">The result type.</typeparam>
public abstract class Node<T> : Node
{
    /// <summary>
    /// Gets the completed result of this node.
    ///
    /// If the node is not yet complete, the runtime should suspend flow expansion
    /// and resume/replay the flow when the node has completed.
    /// </summary>
    public abstract ValueTask<T> GetResultAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes Node&lt;T&gt; directly awaitable:
    ///
    /// var result = await node;
    /// </summary>
    public ValueTaskAwaiter<T> GetAwaiter()
    {
        return GetResultAsync().GetAwaiter();
    }

    /// <summary>
    /// Waits for the node without caring about the result.
    /// </summary>
    public sealed override async ValueTask WaitAsync(
        CancellationToken cancellationToken = default)
    {
        _ = await GetResultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

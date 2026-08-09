using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Represents a durable node in a flow's directed acyclic graph.
/// </summary>
public abstract class Node
{
    public abstract NodeId Id { get; }

    public abstract string Name { get; }

    public abstract NodeKind Kind { get; }

    /// <summary>
    /// Waits until this node reaches a terminal state without returning a result.
    /// </summary>
    public abstract ValueTask WaitAsync(
        CancellationToken cancellationToken = default);

    public override string ToString()
    {
        return $"{Name} ({Id})";
    }
}

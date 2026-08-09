namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Represents a barrier that observes all of its input nodes.
/// </summary>
public abstract class WaitAllNode : WaitNode<WaitAllResult>
{
    public abstract IReadOnlyList<Node> Inputs { get; }

    public abstract BarrierCompletionMode CompletionMode { get; }
}

namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Represents a barrier that selects one of its input nodes.
/// </summary>
public abstract class WaitAnyNode : WaitNode<WaitAnyResult>
{
    public abstract IReadOnlyList<Node> Inputs { get; }

    public abstract BarrierCompletionMode CompletionMode { get; }
}

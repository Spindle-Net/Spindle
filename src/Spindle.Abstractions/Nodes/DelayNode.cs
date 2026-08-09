using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Nodes;

/// <summary>
/// Represents a durable timer wait.
/// </summary>
public abstract class DelayNode : WaitNode<Unit>
{
}

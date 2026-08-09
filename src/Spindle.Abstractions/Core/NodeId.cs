namespace Spindle.Abstractions.Core;

/// <summary>
/// Identifies a node within a flow instance.
/// </summary>
public readonly record struct NodeId(string Value)
{
    public override string ToString() => Value;
}

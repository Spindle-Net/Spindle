namespace Spindle.Abstractions.Core;

public readonly record struct FlowVersion(string Value)
{
    public override string ToString() => Value;
}
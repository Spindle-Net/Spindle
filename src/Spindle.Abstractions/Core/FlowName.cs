namespace Spindle.Abstractions.Core;

public readonly record struct FlowName(string Value)
{
    public override string ToString() => Value;
}
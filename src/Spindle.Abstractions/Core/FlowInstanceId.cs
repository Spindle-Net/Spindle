namespace Spindle.Abstractions.Core;

public readonly record struct FlowInstanceId(string Value)
{
    public override string ToString() => Value;
}
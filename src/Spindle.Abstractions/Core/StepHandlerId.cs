namespace Spindle.Abstractions.Core;

public readonly record struct StepHandlerId(string Value)
{
    public override string ToString() => Value;
}
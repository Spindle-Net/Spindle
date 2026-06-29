namespace Spindle.Abstractions.Core;

public readonly record struct StepId(string Value)
{
    public override string ToString() => Value;
}
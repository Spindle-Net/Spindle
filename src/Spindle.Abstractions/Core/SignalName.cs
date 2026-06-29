namespace Spindle.Abstractions.Core;

public readonly record struct SignalName(string Value)
{
    public override string ToString() => Value;
}
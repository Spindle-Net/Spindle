namespace Spindle.Abstractions.Core;

public readonly record struct CorrelationKey(string Value)
{
    public override string ToString() => Value;
}
namespace Spindle.Abstractions.Core;

public readonly record struct ApplicationName(string Value)
{
    public override string ToString() => Value;
}
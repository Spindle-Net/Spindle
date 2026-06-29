namespace Spindle.Abstractions.Core;

public readonly record struct QueueName(string Value)
{
    public override string ToString() => Value;
}
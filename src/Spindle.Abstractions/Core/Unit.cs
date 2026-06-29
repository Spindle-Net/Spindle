namespace Spindle.Abstractions.Core;

public readonly record struct Unit
{
    public static Unit Value { get; } = new();
}
namespace Spindle.Abstractions.Core;

public readonly record struct StepAttemptId(string Value)
{
    public override string ToString() => Value;
}
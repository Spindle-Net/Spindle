namespace Spindle.Abstractions.Policies;

public sealed record TimeoutPolicy
{
    public required TimeSpan Timeout { get; init; }

    public TimeoutBehavior Behavior { get; init; }
        = TimeoutBehavior.Fail;
}
namespace Spindle.Abstractions.Policies;

public sealed record TimeoutPolicy
{
    public required TimeSpan Timeout { get; init; }

    /// <summary>Time allowed for cooperative cleanup after cancellation, before abandoning the callback.</summary>
    public TimeSpan CancellationGracePeriod { get; init; } = TimeSpan.FromSeconds(5);

    public TimeoutBehavior Behavior { get; init; }
        = TimeoutBehavior.Fail;
}
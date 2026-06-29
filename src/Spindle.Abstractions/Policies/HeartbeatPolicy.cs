namespace Spindle.Abstractions.Policies;

public sealed record HeartbeatPolicy
{
    public TimeSpan Timeout { get; init; }
        = TimeSpan.FromMinutes(1);

    public TimeSpan? SuggestedInterval { get; init; }
}
namespace Spindle.Abstractions.Policies;

public sealed record RetryPolicy
{
    public int MaxAttempts { get; init; } = 3;

    public TimeSpan InitialDelay { get; init; }
        = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; }
        = TimeSpan.FromMinutes(1);

    public RetryBackoffKind BackoffKind { get; init; }
        = RetryBackoffKind.Exponential;

    public double JitterFactor { get; init; } = 0.2;

    public static RetryPolicy None { get; } = new()
    {
        MaxAttempts = 1
    };

    public static RetryPolicy Exponential(int maxAttempts)
    {
        return new RetryPolicy
        {
            MaxAttempts = maxAttempts,
            BackoffKind = RetryBackoffKind.Exponential
        };
    }
}
namespace Spindle.Abstractions.Policies;

public sealed record RetryPolicy
{
    /// <summary>Maximum executions per retry budget, including the initial attempt.</summary>
    public int MaxAttempts { get; init; } = 3;

    public TimeSpan InitialDelay { get; init; }
        = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; }
        = TimeSpan.FromMinutes(1);

    public RetryBackoffKind BackoffKind { get; init; }
        = RetryBackoffKind.Exponential;

    /// <summary>Uniform multiplicative jitter in the range 1 minus this factor to 1 plus this factor.</summary>
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
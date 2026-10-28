namespace Spindle.Abstractions.Policies;

/// <summary>
/// Describes how a failed step is retried.
/// </summary>
/// <remarks>
/// For failed attempt <c>n</c> of the current retry budget, the delay before the next attempt is
/// <see cref="InitialDelay"/> (fixed), <c>InitialDelay × n</c> (linear) or <c>InitialDelay × 2^(n−1)</c>
/// (exponential). The delay is then multiplied by a random factor in <c>1 ± JitterFactor</c> and
/// clamped to <c>[0, MaxDelay]</c>.
/// </remarks>
/// <example>
/// <code>
/// context.Step("charge", "Charge card", ChargeAsync)
///     .WithRetry(RetryPolicy.Exponential(3));
///
/// context.Step("sync", "Sync inventory", SyncAsync)
///     .WithRetry(new RetryPolicy
///     {
///         MaxAttempts = 5,
///         BackoffKind = RetryBackoffKind.Linear,
///         InitialDelay = TimeSpan.FromSeconds(10),
///         MaxDelay = TimeSpan.FromMinutes(2),
///         JitterFactor = 0
///     });
/// </code>
/// </example>
public sealed record RetryPolicy
{
    /// <summary>
    /// The maximum number of executions, including the initial attempt. Must be at least 1.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// The base delay used by the backoff calculation. Must not be negative.
    /// </summary>
    public TimeSpan InitialDelay { get; init; }
        = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The upper bound for any single retry delay. Must be at least <see cref="InitialDelay"/>.
    /// </summary>
    public TimeSpan MaxDelay { get; init; }
        = TimeSpan.FromMinutes(1);

    public RetryBackoffKind BackoffKind { get; init; }
        = RetryBackoffKind.Exponential;

    /// <summary>
    /// The relative amount of random jitter applied to each delay, between 0 and 1.
    /// </summary>
    public double JitterFactor { get; init; } = 0.2;

    /// <summary>
    /// A policy that allows a single execution and never retries.
    /// </summary>
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

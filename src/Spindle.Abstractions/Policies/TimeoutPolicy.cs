namespace Spindle.Abstractions.Policies;

/// <summary>
/// Limits how long a single step attempt may run.
/// </summary>
/// <remarks>
/// When <see cref="Timeout"/> elapses, the step's cancellation token is cancelled and the callback gets
/// <see cref="CancellationGracePeriod"/> to clean up. The attempt is then treated as timed out, even if the
/// callback returns a result during cleanup, and <see cref="Behavior"/> decides what happens next.
/// A callback that ignores cancellation is abandoned rather than stopped: it keeps running in the process,
/// may still produce side effects, and can overlap a retry of the same step.
/// </remarks>
/// <example>
/// <code>
/// context.Step("fetch", "Fetch rates", ctx => FetchRatesAsync(ctx.CancellationToken))
///     .WithTimeout(new TimeoutPolicy { Timeout = TimeSpan.FromSeconds(30) })
///     .WithRetry(RetryPolicy.Exponential(3));
/// </code>
/// </example>
public sealed record TimeoutPolicy
{
    /// <summary>
    /// The maximum duration of one attempt. Must be positive.
    /// </summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>
    /// How long to wait for the callback to observe cancellation after the timeout. Must not be negative.
    /// </summary>
    public TimeSpan CancellationGracePeriod { get; init; }
        = TimeSpan.FromSeconds(5);

    public TimeoutBehavior Behavior { get; init; }
        = TimeoutBehavior.Fail;
}

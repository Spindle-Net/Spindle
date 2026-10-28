using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Steps;

namespace Spindle;

/// <summary>
/// Validation and scheduling arithmetic for step retry and timeout policies.
/// </summary>
internal static class StepPolicies
{
    public static void Validate(StepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Retry is { } retry)
        {
            Validate(retry);
        }

        if (options.Timeout is { } timeout)
        {
            Validate(timeout);
        }
    }

    public static void Validate(RetryPolicy retry)
    {
        if (retry.MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retry),
                retry.MaxAttempts,
                $"{nameof(RetryPolicy.MaxAttempts)} must be at least 1.");
        }

        if (retry.InitialDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retry),
                retry.InitialDelay,
                $"{nameof(RetryPolicy.InitialDelay)} must not be negative.");
        }

        if (retry.MaxDelay < retry.InitialDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retry),
                retry.MaxDelay,
                $"{nameof(RetryPolicy.MaxDelay)} must be at least {nameof(RetryPolicy.InitialDelay)}.");
        }

        if (!double.IsFinite(retry.JitterFactor) || retry.JitterFactor is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retry),
                retry.JitterFactor,
                $"{nameof(RetryPolicy.JitterFactor)} must be between 0 and 1.");
        }

        if (!Enum.IsDefined(retry.BackoffKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(retry),
                retry.BackoffKind,
                $"{nameof(RetryPolicy.BackoffKind)} is not a defined {nameof(RetryBackoffKind)} value.");
        }
    }

    public static void Validate(TimeoutPolicy timeout)
    {
        if (timeout.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout.Timeout,
                $"{nameof(TimeoutPolicy.Timeout)} must be positive.");
        }

        if (timeout.CancellationGracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout.CancellationGracePeriod,
                $"{nameof(TimeoutPolicy.CancellationGracePeriod)} must not be negative.");
        }

        if (!Enum.IsDefined(timeout.Behavior))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout.Behavior,
                $"{nameof(TimeoutPolicy.Behavior)} is not a defined {nameof(TimeoutBehavior)} value.");
        }
    }

    /// <summary>
    /// Returns whether another attempt is allowed after <paramref name="failedAttempt"/> attempts of the
    /// current budget have failed. A missing policy allows a single attempt.
    /// </summary>
    public static bool CanRetry(RetryPolicy? retry, int failedAttempt)
        => retry is not null && failedAttempt < retry.MaxAttempts;

    /// <summary>
    /// Calculates the delay after failed attempt <paramref name="failedAttempt"/> (starting at 1) of the
    /// current retry budget.
    /// </summary>
    /// <param name="jitterSample">A uniformly distributed value in [0, 1) that selects the jitter.</param>
    public static TimeSpan GetRetryDelay(
        RetryPolicy retry,
        int failedAttempt,
        double jitterSample)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempt, 1);

        if (retry.InitialDelay == TimeSpan.Zero || retry.MaxDelay == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        // Work in double ticks: large attempt numbers saturate to infinity instead of overflowing,
        // and the clamp below brings them back to MaxDelay.
        var multiplier = retry.BackoffKind switch
        {
            RetryBackoffKind.Fixed => 1d,
            RetryBackoffKind.Linear => failedAttempt,
            RetryBackoffKind.Exponential => Math.Pow(2, failedAttempt - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(retry), retry.BackoffKind, null)
        };
        var jitter = 1 + (2 * jitterSample - 1) * retry.JitterFactor;
        var ticks = retry.InitialDelay.Ticks * multiplier * jitter;

        return ticks >= retry.MaxDelay.Ticks
            ? retry.MaxDelay
            : TimeSpan.FromTicks((long)Math.Max(0, ticks));
    }

    /// <summary>
    /// Adds <paramref name="delay"/> to <paramref name="time"/>, saturating at <see cref="DateTimeOffset.MaxValue"/>.
    /// </summary>
    public static DateTimeOffset AddSaturating(DateTimeOffset time, TimeSpan delay)
        => delay > DateTimeOffset.MaxValue - time
            ? DateTimeOffset.MaxValue
            : time.Add(delay);

    /// <summary>
    /// Adds two durations, saturating at <see cref="TimeSpan.MaxValue"/>.
    /// </summary>
    public static TimeSpan AddSaturating(TimeSpan left, TimeSpan right)
        => right > TimeSpan.MaxValue - left
            ? TimeSpan.MaxValue
            : left + right;
}

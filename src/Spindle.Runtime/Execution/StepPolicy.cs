using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Steps;

namespace Spindle;

internal static class StepPolicy
{
    public static void Validate(StepOptions options)
    {
        if (options.Retry is { } retry &&
            (retry.MaxAttempts < 1 || retry.InitialDelay < TimeSpan.Zero ||
             retry.MaxDelay < retry.InitialDelay || !Enum.IsDefined(retry.BackoffKind) ||
             !double.IsFinite(retry.JitterFactor) || retry.JitterFactor < 0 || retry.JitterFactor > 1))
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid retry policy.");
        if (options.Timeout is { } timeout &&
            (timeout.Timeout <= TimeSpan.Zero || timeout.CancellationGracePeriod < TimeSpan.Zero ||
             !Enum.IsDefined(timeout.Behavior)))
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid timeout policy.");
    }

    public static DateTimeOffset DueAt(DateTimeOffset now, TimeSpan delay) =>
        delay.Ticks > (DateTimeOffset.MaxValue - now).Ticks ? DateTimeOffset.MaxValue : now.Add(delay);

    public static async Task DelayAsync(TimeSpan delay, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var dueAt = DueAt(timeProvider.GetUtcNow(), delay);
        while (dueAt > timeProvider.GetUtcNow())
        {
            var remaining = dueAt - timeProvider.GetUtcNow();
            // System timers accept only bounded durations; preserve arbitrary policy durations by waiting in chunks.
            await Task.Delay(remaining > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : remaining,
                timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    public static TimeSpan Delay(RetryPolicy policy, int failedAttempt)
    {
        var multiplier = policy.BackoffKind switch
        {
            RetryBackoffKind.Fixed => 1d,
            RetryBackoffKind.Linear => failedAttempt,
            RetryBackoffKind.Exponential => Math.Pow(2, failedAttempt - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
        // Saturate before overflow; sample once and persist the resulting due time.
        var ticks = policy.InitialDelay.Ticks == 0 ? 0 : policy.InitialDelay.Ticks * multiplier;
        var jitter = 1 + (Random.Shared.NextDouble() * 2 - 1) * policy.JitterFactor;
        var bounded = jitter == 0 ? 0 : Math.Clamp(ticks * jitter, 0, policy.MaxDelay.Ticks);
        return TimeSpan.FromTicks((long)Math.Min(bounded, TimeSpan.MaxValue.Ticks - 1024d));
    }
}

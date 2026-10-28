namespace Spindle;

internal static class TimeProviderDelays
{
    // Timers reject due times above roughly 49 days, so longer waits are split into chunks.
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromDays(30);

    /// <summary>
    /// Waits for <paramref name="delay"/> on <paramref name="timeProvider"/>, including delays that are too
    /// long for a single timer.
    /// </summary>
    public static async Task DelayAsync(
        this TimeProvider timeProvider,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        while (delay > MaxTimerDelay)
        {
            await Task.Delay(MaxTimerDelay, timeProvider, cancellationToken).ConfigureAwait(false);
            delay -= MaxTimerDelay;
        }

        await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until <paramref name="timeProvider"/> reaches <paramref name="dueAt"/>.
    /// </summary>
    public static async Task DelayUntilAsync(
        this TimeProvider timeProvider,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining;
        while ((remaining = dueAt - timeProvider.GetUtcNow()) > TimeSpan.Zero)
        {
            await Task.Delay(
                    remaining < MaxTimerDelay ? remaining : MaxTimerDelay,
                    timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

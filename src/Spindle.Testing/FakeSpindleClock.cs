namespace Spindle.Testing;

/// <summary>
/// A manually controlled <see cref="TimeProvider"/>. Timers created from it, including those behind
/// <see cref="Task.Delay(TimeSpan, TimeProvider)"/> and <see cref="CancellationTokenSource(TimeSpan, TimeProvider)"/>,
/// fire when the clock is moved to or past their due time.
/// </summary>
public sealed class FakeSpindleClock(DateTimeOffset? initialUtcNow = null) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _utcNow = initialUtcNow ?? DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        return UtcNow.UtcTicks;
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }
    }

    /// <summary>
    /// The number of timers that are scheduled to fire.
    /// </summary>
    public int ActiveTimerCount
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(timer => timer.DueAt is not null);
            }
        }
    }

    public DateTimeOffset SetUtcNow(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            _utcNow = utcNow;
        }

        FireDueTimers();
        return utcNow;
    }

    public DateTimeOffset Advance(TimeSpan duration)
    {
        lock (_gate)
        {
            _utcNow = _utcNow.Add(duration);
        }

        FireDueTimers();
        return UtcNow;
    }

    public DateTimeOffset AdvanceBy(TimeSpan duration)
    {
        return Advance(duration);
    }

    public DateTimeOffset AdvanceTo(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            if (utcNow < _utcNow)
            {
                throw new InvalidOperationException("Fake clock cannot move backwards.");
            }

            _utcNow = utcNow;
        }

        FireDueTimers();
        return utcNow;
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new FakeTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    private void FireDueTimers()
    {
        // Callbacks run outside the lock because they commonly create or change timers themselves.
        while (TryTakeDueTimer(out var timer))
        {
            timer.Callback(timer.State);
        }
    }

    private bool TryTakeDueTimer(out FakeTimer timer)
    {
        lock (_gate)
        {
            timer = _timers
                .Where(candidate => candidate.DueAt <= _utcNow)
                .OrderBy(candidate => candidate.DueAt)
                .FirstOrDefault()!;

            if (timer is null)
            {
                return false;
            }

            timer.DueAt = timer.Period is { } period && period > TimeSpan.Zero
                ? timer.DueAt!.Value.Add(period)
                : null;
            return true;
        }
    }

    private sealed class FakeTimer(
        FakeSpindleClock clock,
        TimerCallback callback,
        object? state)
        : ITimer
    {
        private bool _disposed;

        public TimerCallback Callback => callback;

        public object? State => state;

        public DateTimeOffset? DueAt { get; set; }

        public TimeSpan? Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ValidateDuration(dueTime, nameof(dueTime));
            ValidateDuration(period, nameof(period));

            lock (clock._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._utcNow.Add(dueTime);
                Period = period == Timeout.InfiniteTimeSpan ? null : period;
            }

            clock.FireDueTimers();
            return true;
        }

        public void Dispose()
        {
            lock (clock._gate)
            {
                _disposed = true;
                DueAt = null;
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private static void ValidateDuration(TimeSpan duration, string parameterName)
        {
            if (duration < TimeSpan.Zero && duration != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}

namespace Spindle.Testing;

/// <summary>A manually advanced clock whose timers fire when time advances.</summary>
public sealed class FakeSpindleClock(DateTimeOffset? initialUtcNow = null) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ClockTimer> _timers = [];
    private DateTimeOffset _utcNow = initialUtcNow ?? DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
    public DateTimeOffset UtcNow { get { lock (_gate) return _utcNow; } }

    /// <summary>Returns the due times of currently scheduled timers, for deterministic test synchronization.</summary>
    public IReadOnlyList<DateTimeOffset> GetPendingTimers()
    {
        lock (_gate) return _timers.Where(t => t.DueAt.HasValue).Select(t => t.DueAt!.Value).ToArray();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ClockTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
            timer.Change(dueTime, period);
        }
        return timer;
    }

    public DateTimeOffset SetUtcNow(DateTimeOffset utcNow)
    {
        lock (_gate) _utcNow = utcNow;
        FireTimers();
        return UtcNow;
    }

    public DateTimeOffset Advance(TimeSpan duration) => SetUtcNow(UtcNow.Add(duration));
    public DateTimeOffset AdvanceBy(TimeSpan duration) => Advance(duration);
    public DateTimeOffset AdvanceTo(DateTimeOffset utcNow)
    {
        if (utcNow < UtcNow) throw new InvalidOperationException("Fake clock cannot move backwards.");
        return SetUtcNow(utcNow);
    }

    private void FireTimers()
    {
        while (true)
        {
            ClockTimer? timer;
            lock (_gate)
            {
                timer = _timers.Where(t => t.DueAt <= _utcNow).OrderBy(t => t.DueAt).FirstOrDefault();
                if (timer is null) return;
                timer.DueAt = timer.Period > TimeSpan.Zero ? timer.DueAt!.Value.Add(timer.Period) : null;
            }
            timer.Fire();
        }
    }

    private sealed class ClockTimer(FakeSpindleClock clock, TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public DateTimeOffset? DueAt { get; set; }
        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(dueTime));
            if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(period));
            lock (clock._gate)
            {
                if (_disposed) return false;
                Period = period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._utcNow.Add(dueTime);
                return true;
            }
        }

        public void Fire()
        {
            lock (clock._gate) { if (_disposed) return; }
            callback(state);
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

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

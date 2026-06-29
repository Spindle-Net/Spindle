namespace Spindle.Testing;

public sealed class FakeSpindleClock(DateTimeOffset? initialUtcNow = null) : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _utcNow = initialUtcNow ?? DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
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

    public DateTimeOffset SetUtcNow(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            _utcNow = utcNow;
            return _utcNow;
        }
    }

    public DateTimeOffset Advance(TimeSpan duration)
    {
        lock (_gate)
        {
            _utcNow = _utcNow.Add(duration);
            return _utcNow;
        }
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
            return _utcNow;
        }
    }
}

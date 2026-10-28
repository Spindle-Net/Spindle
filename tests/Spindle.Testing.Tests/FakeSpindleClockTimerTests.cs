using Spindle.Testing;
using Xunit;

namespace Spindle.Testing.Tests;

public sealed class FakeSpindleClockTimerTests
{
    [Fact]
    public async Task DelayFiresOnlyWhenDueAndCancellationRemovesTimer()
    {
        var clock = new FakeSpindleClock();
        var delay = Task.Delay(TimeSpan.FromSeconds(10), clock);
        clock.AdvanceBy(TimeSpan.FromSeconds(9));
        Assert.False(delay.IsCompleted);
        clock.AdvanceBy(TimeSpan.FromSeconds(1));
        await delay;
        using var cancellation = new CancellationTokenSource();
        var cancelled = Task.Delay(TimeSpan.FromHours(1), clock, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Empty(clock.GetPendingTimers());
    }

    [Fact]
    public void PeriodicTimersCanChangeAndDisposeDuringCallbacks()
    {
        var clock = new FakeSpindleClock();
        var calls = 0;
        using var timer = clock.CreateTimer(_ => calls++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        clock.AdvanceBy(TimeSpan.FromSeconds(5));
        Assert.Equal(3, calls);
        timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        clock.AdvanceBy(TimeSpan.FromSeconds(1));
        Assert.Equal(4, calls);
        timer.Dispose();
        clock.AdvanceBy(TimeSpan.FromHours(1));
        Assert.Equal(4, calls);
    }
}

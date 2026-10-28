using Spindle.Persistence.InMemory;
using Spindle.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spindle.Runtime.Tests;

public class TestBase
{
    protected static (RuntimeSpindleRuntime Runtime, InMemorySpindleStore Store, JsonSpindleSerializer Serializer) CreateRuntime()
    {
        var defaultNow = DateTimeOffset.Parse("2026-06-28T12:00:00Z");
        var (runtime, store, serializer, _) = CreateRuntime(defaultNow);
        return (runtime, store, serializer);
    }

    protected static (RuntimeSpindleRuntime Runtime, InMemorySpindleStore Store, JsonSpindleSerializer Serializer, FakeSpindleClock Clock) CreateRuntime(
        DateTimeOffset initialUtcNow)
    {
        var store = new InMemorySpindleStore();
        var serializer = new JsonSpindleSerializer();
        var clock = new FakeSpindleClock(initialUtcNow);
        var runtime = new RuntimeSpindleRuntime(
            store,
            options: new RuntimeSpindleOptions
            {
                TimeProvider = clock,
                Serializer = serializer
            });

        return (runtime, store, serializer, clock);
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Waits in real time until the clock has at least <paramref name="count"/> scheduled timers.
    /// </summary>
    protected static async Task WaitForTimersAsync(FakeSpindleClock clock, int count = 1)
    {
        using var cancellation = new CancellationTokenSource(TestTimeout);
        while (clock.ActiveTimerCount < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2), cancellation.Token);
        }
    }

    /// <summary>
    /// Advances the clock by <paramref name="step"/> whenever a timer is waiting, until <paramref name="task"/> finishes.
    /// </summary>
    protected static async Task<T> DriveAsync<T>(Task<T> task, FakeSpindleClock clock, TimeSpan step)
    {
        using var cancellation = new CancellationTokenSource(TestTimeout);
        while (!task.IsCompleted)
        {
            if (clock.ActiveTimerCount > 0)
            {
                clock.Advance(step);
            }

            await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(2), cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
        }

        return await task;
    }
}

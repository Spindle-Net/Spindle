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
}

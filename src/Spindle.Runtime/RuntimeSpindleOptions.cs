using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle;

public sealed class RuntimeSpindleOptions
{
    public ApplicationName ApplicationName { get; init; } = new("spindle");

    public TimeProvider? TimeProvider { get; init; }

    public ISpindleSerializer? Serializer { get; init; }

    public IServiceProvider? Services { get; init; }

    public StepHandlerRegistry? StepHandlers { get; init; }

    public string WorkerId { get; init; } =
        $"local-{Environment.MachineName}";

    public TimeSpan StepLeaseDuration { get; init; } =
        TimeSpan.FromSeconds(30);

    public int MaxRunIterations { get; init; } = 100;
}

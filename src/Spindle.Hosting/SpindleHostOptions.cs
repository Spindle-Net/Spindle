namespace Spindle.Hosting;

public sealed class SpindleHostOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public int MaxFlowInstancesPerTick { get; set; } = 100;

    public int MaxStepsPerFlowPerTick { get; set; } = 1;

    public int MaxConcurrentFlowInstances { get; set; } =
        Math.Max(1, Environment.ProcessorCount);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    public string WorkerId { get; set; } =
        $"local-{Environment.MachineName}";
}

namespace Spindle.Hosting;

public readonly record struct SpindlePumpResult(
    int ScheduledFlows,
    int InFlightFlows,
    int ReplayedFlows,
    int ExecutedSteps,
    int FiredTimers,
    int CompletedFlows,
    int FailedFlows)
{
    public bool HasProgress =>
        ReplayedFlows > 0 ||
        ExecutedSteps > 0 ||
        FiredTimers > 0 ||
        CompletedFlows > 0 ||
        FailedFlows > 0;

    public static SpindlePumpResult Empty { get; } = new(
        ScheduledFlows: 0,
        InFlightFlows: 0,
        ReplayedFlows: 0,
        ExecutedSteps: 0,
        FiredTimers: 0,
        CompletedFlows: 0,
        FailedFlows: 0);

    public SpindlePumpResult Add(
        SpindlePumpResult other)
    {
        return new SpindlePumpResult(
            ScheduledFlows + other.ScheduledFlows,
            other.InFlightFlows,
            ReplayedFlows + other.ReplayedFlows,
            ExecutedSteps + other.ExecutedSteps,
            FiredTimers + other.FiredTimers,
            CompletedFlows + other.CompletedFlows,
            FailedFlows + other.FailedFlows);
    }
}

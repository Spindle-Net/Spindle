namespace Spindle;

internal readonly record struct RuntimeInstanceAdvanceResult(
    int ReplayedFlows,
    int ExecutedSteps,
    int CompletedFlows,
    int FailedFlows)
{
    public static RuntimeInstanceAdvanceResult Empty { get; } = new(
        ReplayedFlows: 0,
        ExecutedSteps: 0,
        CompletedFlows: 0,
        FailedFlows: 0);
}

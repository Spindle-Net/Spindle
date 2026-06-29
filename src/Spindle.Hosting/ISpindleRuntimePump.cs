namespace Spindle.Hosting;

public interface ISpindleRuntimePump
{
    ValueTask<SpindlePumpResult> RunOnceAsync(
        CancellationToken cancellationToken = default);

    ValueTask<SpindlePumpResult> RunUntilIdleAsync(
        int maxIterations = 100,
        CancellationToken cancellationToken = default);

    ValueTask<bool> WaitForWakeupAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

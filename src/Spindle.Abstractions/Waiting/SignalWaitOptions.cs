namespace Spindle.Abstractions.Waiting;

public sealed record SignalWaitOptions
{
    public TimeSpan? Timeout { get; init; }

    public bool ConsumeBufferedSignal { get; init; } = true;

    public bool BufferIfNotWaiting { get; init; } = true;
}
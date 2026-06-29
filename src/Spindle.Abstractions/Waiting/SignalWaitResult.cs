namespace Spindle.Abstractions.Waiting;

public sealed record SignalWaitResult<TSignal>
{
    public bool Received { get; init; }

    public bool TimedOut { get; init; }

    public TSignal? Payload { get; init; }
}
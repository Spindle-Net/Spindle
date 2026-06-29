using Spindle.Abstractions.Core;
using Spindle.Abstractions.Policies;

namespace Spindle.Abstractions.Steps;

public sealed record StepOptions
{
    public QueueName? Queue { get; init; }

    public StepDispatchMode DispatchMode { get; init; }
        = StepDispatchMode.LocalWorker;

    public RetryPolicy? Retry { get; init; }

    public TimeoutPolicy? Timeout { get; init; }

    public HeartbeatPolicy? Heartbeat { get; init; }

    public string? IdempotencyKey { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>();
}
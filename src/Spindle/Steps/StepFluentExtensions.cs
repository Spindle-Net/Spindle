using Spindle.Abstractions.Core;
using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle;

public static class StepFluentExtensions
{
    public static StepNode<TResult> OnQueue<TResult>(
        this StepNode<TResult> step,
        QueueName queue)
    {
        return step.WithOptions(options => options with
        {
            Queue = queue,
            DispatchMode = StepDispatchMode.Queued
        });
    }

    public static StepNode<TResult> WithRetry<TResult>(
        this StepNode<TResult> step,
        RetryPolicy retry)
    {
        return step.WithOptions(options => options with { Retry = retry });
    }

    public static StepNode<TResult> WithTimeout<TResult>(
        this StepNode<TResult> step,
        TimeoutPolicy timeout)
    {
        return step.WithOptions(options => options with { Timeout = timeout });
    }

    public static StepNode<TResult> WithHeartbeat<TResult>(
        this StepNode<TResult> step,
        HeartbeatPolicy heartbeat)
    {
        return step.WithOptions(options => options with { Heartbeat = heartbeat });
    }
}

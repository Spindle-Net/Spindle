using Spindle.Abstractions.Core;
using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Steps;

namespace Spindle;

public static class StepFluentExtensions
{
    public static Step OnQueue(
        this Step step,
        QueueName queue)
    {
        return step.WithOptions(options => options with
        {
            Queue = queue,
            DispatchMode = StepDispatchMode.Queued
        });
    }

    public static Step<TResult> OnQueue<TResult>(
        this Step<TResult> step,
        QueueName queue)
    {
        return step.WithOptions(options => options with
        {
            Queue = queue,
            DispatchMode = StepDispatchMode.Queued
        });
    }

    public static Step WithRetry(
        this Step step,
        RetryPolicy retry)
    {
        return step.WithOptions(options => options with { Retry = retry });
    }

    public static Step<TResult> WithRetry<TResult>(
        this Step<TResult> step,
        RetryPolicy retry)
    {
        return step.WithOptions(options => options with { Retry = retry });
    }

    public static Step WithTimeout(
        this Step step,
        TimeoutPolicy timeout)
    {
        return step.WithOptions(options => options with { Timeout = timeout });
    }

    public static Step<TResult> WithTimeout<TResult>(
        this Step<TResult> step,
        TimeoutPolicy timeout)
    {
        return step.WithOptions(options => options with { Timeout = timeout });
    }

    public static Step WithHeartbeat(
        this Step step,
        HeartbeatPolicy heartbeat)
    {
        return step.WithOptions(options => options with { Heartbeat = heartbeat });
    }

    public static Step<TResult> WithHeartbeat<TResult>(
        this Step<TResult> step,
        HeartbeatPolicy heartbeat)
    {
        return step.WithOptions(options => options with { Heartbeat = heartbeat });
    }
}

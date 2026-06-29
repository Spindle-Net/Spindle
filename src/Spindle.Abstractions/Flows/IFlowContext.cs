using Spindle.Abstractions.Core;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Waiting;

namespace Spindle.Abstractions.Flows;

public interface IFlowContext
{
    FlowInstanceId InstanceId { get; }

    FlowName FlowName { get; }

    FlowVersion FlowVersion { get; }

    CancellationToken CancellationToken { get; }

    Step<TResult> Step<TResult>(
        string id,
        string name,
        IReadOnlyList<Step> dependencies,
        StepCallback<TResult> execute,
        StepOptions? options = null);

    Step<TResult> StepHandler<TRequest, TResult>(
        string id,
        string name,
        StepHandlerId handlerId,
        IReadOnlyList<Step> dependencies,
        Func<StepInputs, TRequest> createRequest,
        StepOptions? options = null);

    ValueTask WaitAll(params Step[] steps);

    ValueTask Delay(
        string id,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    ValueTask DelayUntil(
        string id,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken = default);

    ValueTask<TSignal> WaitForSignal<TSignal>(
        SignalName signalName,
        CorrelationKey? correlationKey = null,
        SignalWaitOptions? options = null,
        CancellationToken cancellationToken = default);
}
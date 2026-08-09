using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Waiting;

namespace Spindle.Abstractions.Flows;

public interface IFlowContext
{
    FlowInstanceId InstanceId { get; }

    FlowName FlowName { get; }

    FlowVersion FlowVersion { get; }

    CancellationToken CancellationToken { get; }

    StepNode<TResult> Step<TResult>(
        string id,
        string name,
        IReadOnlyList<Node> dependencies,
        StepCallback<TResult> execute,
        StepOptions? options = null);

    StepNode<TResult> StepHandler<TRequest, TResult>(
        string id,
        string name,
        StepHandlerId handlerId,
        IReadOnlyList<Node> dependencies,
        Func<NodeInputs, TRequest> createRequest,
        StepOptions? options = null);

    WaitAllNode WaitAll(
        string id,
        params Node[] nodes);

    WaitAllNode WaitAll(
        string id,
        string name,
        params Node[] nodes);

    WaitAllNode WaitAll(
        string id,
        BarrierCompletionMode completionMode,
        params Node[] nodes);

    WaitAllNode WaitAll(
        string id,
        string name,
        BarrierCompletionMode completionMode,
        params Node[] nodes);

    WaitAnyNode WaitAny(
        string id,
        params Node[] nodes);

    WaitAnyNode WaitAny(
        string id,
        string name,
        params Node[] nodes);

    WaitAnyNode WaitAny(
        string id,
        BarrierCompletionMode completionMode,
        params Node[] nodes);

    WaitAnyNode WaitAny(
        string id,
        string name,
        BarrierCompletionMode completionMode,
        params Node[] nodes);

    DelayNode Delay(
        string id,
        TimeSpan duration);

    DelayNode Delay(
        string id,
        string name,
        TimeSpan duration);

    DelayNode DelayUntil(
        string id,
        DateTimeOffset dueAt);

    DelayNode DelayUntil(
        string id,
        string name,
        DateTimeOffset dueAt);

    SignalNode<TSignal> WaitForSignal<TSignal>(
        string id,
        string name,
        SignalName signalName,
        CorrelationKey correlationKey,
        SignalWaitOptions? options = null);

    SignalNode<TSignal> WaitForSignal<TSignal>(
        string id,
        SignalName signalName,
        CorrelationKey correlationKey,
        SignalWaitOptions? options = null);
}

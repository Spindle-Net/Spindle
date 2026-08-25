using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Waiting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spindle.Runtime;

internal class NamespacedFlowContextWrapper(string ns, IFlowContext parent) : IFlowContext
{
    public FlowInstanceId InstanceId => parent.InstanceId;

    public FlowName FlowName => parent.FlowName;

    public FlowVersion FlowVersion => parent.FlowVersion;

    public CancellationToken CancellationToken => parent.CancellationToken;

    private string WrapId(string id) => $"{ns}/{id}";

    public DelayNode Delay(string id, TimeSpan duration)
            => parent.Delay(WrapId(id), duration);

    public DelayNode Delay(string id, string name, TimeSpan duration)
            => parent.Delay(WrapId(id), name, duration);

    public DelayNode DelayUntil(string id, DateTimeOffset dueAt)
            => parent.DelayUntil(WrapId(id), dueAt);

    public DelayNode DelayUntil(string id, string name, DateTimeOffset dueAt)
            => parent.DelayUntil(WrapId(id), name, dueAt);

    public ForkNode<TValue> Fork<TValue>(string id, Func<IFlowContext, Task<TValue>> descriptor)
            => parent.Fork(WrapId(id), descriptor);

    public StepNode<TResult> Step<TResult>(string id, string name, IReadOnlyList<Node> dependencies, StepCallback<TResult> execute, StepOptions? options = null)
            => parent.Step(WrapId(id), name, dependencies, execute, options);

    public StepNode<TResult> StepHandler<TRequest, TResult>(string id, string name, StepHandlerId handlerId, IReadOnlyList<Node> dependencies, Func<NodeInputs, TRequest> createRequest, StepOptions? options = null)
            => parent.StepHandler<TRequest, TResult>(WrapId(id), name, handlerId, dependencies, createRequest, options);

    public WaitAllNode WaitAll(string id, params Node[] nodes)
            => parent.WaitAll(WrapId(id), nodes);

    public WaitAllNode WaitAll(string id, string name, params Node[] nodes)
            => parent.WaitAll(WrapId(id), name, nodes);

    public WaitAllNode WaitAll(string id, BarrierCompletionMode completionMode, params Node[] nodes)
            => parent.WaitAll(WrapId(id), completionMode, nodes);

    public WaitAllNode WaitAll(string id, string name, BarrierCompletionMode completionMode, params Node[] nodes)
            => parent.WaitAll(WrapId(id), name, completionMode, nodes);

    public WaitAnyNode WaitAny(string id, params Node[] nodes)
            => parent.WaitAny(WrapId(id), nodes);

    public WaitAnyNode WaitAny(string id, string name, params Node[] nodes)
            => parent.WaitAny(WrapId(id), name, nodes);

    public WaitAnyNode WaitAny(string id, BarrierCompletionMode completionMode, params Node[] nodes)
            => parent.WaitAny(WrapId(id), completionMode, nodes);

    public WaitAnyNode WaitAny(string id, string name, BarrierCompletionMode completionMode, params Node[] nodes)
            => parent.WaitAny(WrapId(id), name, completionMode, nodes);

    public SignalNode<TSignal> WaitForSignal<TSignal>(string id, string name, SignalName signalName, CorrelationKey correlationKey, SignalWaitOptions? options = null)
            => parent.WaitForSignal<TSignal>(WrapId(id), name, signalName, correlationKey, options);

    public SignalNode<TSignal> WaitForSignal<TSignal>(string id, SignalName signalName, CorrelationKey correlationKey, SignalWaitOptions? options = null)
            => parent.WaitForSignal<TSignal>(WrapId(id), signalName, correlationKey, options);

    public ConditionNode WaitForCondition(string id, string name, TimeSpan pollingInterval, IReadOnlyList<Node> dependencies, ConditionCallback condition)
            => parent.WaitForCondition(WrapId(id), name, pollingInterval, dependencies, condition);

}

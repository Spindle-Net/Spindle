using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Waiting;
using Spindle.Persistence;
using Spindle.Persistence.Nodes;
using Spindle.Persistence.Signals;
using Spindle.Persistence.Timers;
using Spindle.Runtime.Nodes;
using Spindle.Runtime;

namespace Spindle;

internal sealed class RuntimeFlowContext(
    ISpindleStore store,
    FlowExecutionSession session,
    FlowDescriptor descriptor,
    ISpindleSerializer serializer,
    TimeProvider timeProvider,
    StepHandlerRegistry stepHandlers,
    IServiceProvider services,
    CancellationToken cancellationToken)
    : IFlowContext
{
    public FlowInstanceId InstanceId => session.FlowInstanceId;

    public FlowName FlowName => descriptor.FlowName;

    public FlowVersion FlowVersion => descriptor.FlowVersion;

    public CancellationToken CancellationToken => cancellationToken;

    public StepNode<TResult> Step<TResult>(
        string id,
        string name,
        IReadOnlyList<Node> dependencies,
        StepCallback<TResult> execute,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(execute);

        return DeclareStepNode(id, name, handlerId: null, dependencies, execute, options);
    }

    public StepNode<TResult> StepHandler<TRequest, TResult>(
        string id,
        string name,
        StepHandlerId handlerId,
        IReadOnlyList<Node> dependencies,
        Func<NodeInputs, TRequest> createRequest,
        StepOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(createRequest);

        async ValueTask<TResult> Execute(NodeInputs inputs, IStepExecutionContext context)
        {
            var handler = stepHandlers.Resolve<TRequest, TResult>(handlerId, services)
                ?? services.GetService(typeof(IStepHandler<TRequest, TResult>))
                    as IStepHandler<TRequest, TResult>;

            if (handler is null)
            {
                throw new NotSupportedException(
                    $"Step handler '{handlerId}' is not registered in the current service provider.");
            }

            return await handler.ExecuteAsync(createRequest(inputs), context)
                .ConfigureAwait(false);
        }

        return DeclareStepNode(id, name, handlerId, dependencies, Execute, options);
    }

    public WaitAllNode WaitAll(
        string id,
        params Node[] nodes)
        => WaitAll(id, id, BarrierCompletionMode.Terminal, nodes);

    public WaitAllNode WaitAll(
        string id,
        string name,
        params Node[] nodes)
        => WaitAll(id, name, BarrierCompletionMode.Terminal, nodes);

    public WaitAllNode WaitAll(
        string id,
        BarrierCompletionMode completionMode,
        params Node[] nodes)
        => WaitAll(id, id, completionMode, nodes);

    public WaitAllNode WaitAll(
        string id,
        string name,
        BarrierCompletionMode completionMode,
        params Node[] nodes)
    {
        var inputs = ValidateBarrierInputs(nodes);
        var nodeId = new NodeId(id);
        var dependencyMode = completionMode == BarrierCompletionMode.Terminal
            ? DependencySatisfactionMode.AllTerminal
            : DependencySatisfactionMode.AllSucceeded;

        DeclareBarrier(nodeId, name, NodeKind.WaitAll, inputs, dependencyMode);

        return new RuntimeWaitAllNode(
            CreateState<WaitAllResult>(nodeId, name, NodeKind.WaitAll),
            inputs,
            completionMode);
    }

    public WaitAnyNode WaitAny(
        string id,
        params Node[] nodes)
        => WaitAny(id, id, BarrierCompletionMode.Terminal, nodes);

    public WaitAnyNode WaitAny(
        string id,
        string name,
        params Node[] nodes)
        => WaitAny(id, name, BarrierCompletionMode.Terminal, nodes);

    public WaitAnyNode WaitAny(
        string id,
        BarrierCompletionMode completionMode,
        params Node[] nodes)
        => WaitAny(id, id, completionMode, nodes);

    public WaitAnyNode WaitAny(
        string id,
        string name,
        BarrierCompletionMode completionMode,
        params Node[] nodes)
    {
        var inputs = ValidateBarrierInputs(nodes);
        var nodeId = new NodeId(id);
        var dependencyMode = completionMode == BarrierCompletionMode.Terminal
            ? DependencySatisfactionMode.AnyTerminal
            : DependencySatisfactionMode.AnySucceeded;

        DeclareBarrier(nodeId, name, NodeKind.WaitAny, inputs, dependencyMode);

        return new RuntimeWaitAnyNode(
            CreateState<WaitAnyResult>(nodeId, name, NodeKind.WaitAny),
            inputs,
            completionMode);
    }

    public DelayNode Delay(string id, TimeSpan duration)
        => Delay(id, id, duration);

    public DelayNode Delay(string id, string name, TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        return DelayUntil(id, name, timeProvider.GetUtcNow().Add(duration));
    }

    public DelayNode DelayUntil(string id, DateTimeOffset dueAt)
        => DelayUntil(id, id, dueAt);

    public DelayNode DelayUntil(string id, string name, DateTimeOffset dueAt)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nodeId = new NodeId(id);
        if (!session.TryGetNode(nodeId, out _))
        {
            var now = timeProvider.GetUtcNow();
            session.TryDeclareNode(
                new NodeInstanceRecord
                {
                    FlowInstanceId = InstanceId,
                    NodeId = nodeId,
                    Name = name,
                    Kind = NodeKind.Timer,
                    Status = NodeStatus.Waiting,
                    DispatchMode = StepDispatchMode.Immediate,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new TimerNodeInitialization(
                    new TimerRecord
                    {
                        FlowInstanceId = InstanceId,
                        NodeId = nodeId,
                        DueAt = dueAt,
                        CreatedAt = now
                    }));
        }

        return new RuntimeDelayNode(CreateState<Unit>(nodeId, name, NodeKind.Timer));
    }

    public SignalNode<TSignal> WaitForSignal<TSignal>(
        string id,
        SignalName signalName,
        CorrelationKey correlationKey,
        SignalWaitOptions? options = null)
        => WaitForSignal<TSignal>(id, id, signalName, correlationKey, options);

    public SignalNode<TSignal> WaitForSignal<TSignal>(
        string id,
        string name,
        SignalName signalName,
        CorrelationKey correlationKey,
        SignalWaitOptions? options = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nodeId = new NodeId(id);
        if (!session.TryGetNode(nodeId, out _))
        {
            var now = timeProvider.GetUtcNow();
            session.TryDeclareNode(
                new NodeInstanceRecord
                {
                    FlowInstanceId = InstanceId,
                    NodeId = nodeId,
                    Kind = NodeKind.SignalWait,
                    Name = name,
                    Status = NodeStatus.Waiting,
                    CreatedAt = now,
                    UpdatedAt = now,
                    DispatchMode = StepDispatchMode.Immediate
                },
                new SignalNodeInitialization(
                    new SignalWaitRecord
                    {
                        FlowInstanceId = InstanceId,
                        NodeId = nodeId,
                        SignalName = signalName,
                        CorrelationKey = correlationKey,
                        CreatedAt = now,
                        ExpiresAt = options?.Timeout is { } timeout ? now.Add(timeout) : null
                    }));
        }

        return new RuntimeSignalNode<TSignal>(
            CreateState<TSignal?>(nodeId, name, NodeKind.SignalWait),
            signalName,
            correlationKey);
    }

    public ForkNode<TValue> Fork<TValue>(string id, Func<IFlowContext, Task<TValue>> descriptor)
    {
        var nsWrapper = new NamespacedFlowContextWrapper(id, this);
        var task = descriptor(nsWrapper);
        session.RegisterDescriptorAsyncInitialization(task);
        return new RuntimeForkNode<TValue>(new NodeId(id), $"Fork {id}", task);
    }

    private StepNode<TResult> DeclareStepNode<TResult>(
        string id,
        string name,
        StepHandlerId? handlerId,
        IReadOnlyList<Node> dependencies,
        StepCallback<TResult> execute,
        StepOptions? options)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDependencies(dependencies);

        var nodeId = new NodeId(id);
        var stepOptions = options ?? new StepOptions();
        var dependencyIds = dependencies.Select(dependency => dependency.Id).ToArray();
        var dependencyResultTypes = dependencies.Select(GetDependencyResultType).ToArray();

        session.Register(nodeId, dependencyResultTypes, execute);

        if (!session.TryGetNode(nodeId, out _))
        {
            var now = timeProvider.GetUtcNow();
            var status = DependenciesSucceeded(dependencyIds)
                ? NodeStatus.Ready
                : NodeStatus.Pending;

            session.TryDeclareNode(
                new NodeInstanceRecord
                {
                    FlowInstanceId = InstanceId,
                    NodeId = nodeId,
                    Name = name,
                    Kind = NodeKind.Step,
                    Status = status,
                    HandlerId = handlerId,
                    Queue = stepOptions.Queue,
                    DispatchMode = stepOptions.DispatchMode,
                    DependencyMode = DependencySatisfactionMode.AllSucceeded,
                    Dependencies = dependencyIds,
                    CreatedAt = now,
                    UpdatedAt = now
                });
        }

        return new RuntimeStepNode<TResult>(
            CreateState<TResult>(nodeId, name, NodeKind.Step),
            stepOptions);
    }

    private void DeclareBarrier(
        NodeId nodeId,
        string name,
        NodeKind kind,
        IReadOnlyList<Node> inputs,
        DependencySatisfactionMode dependencyMode)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (session.TryGetNode(nodeId, out _))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        session.TryDeclareNode(
            new NodeInstanceRecord
            {
                FlowInstanceId = InstanceId,
                NodeId = nodeId,
                Name = name,
                Kind = kind,
                Status = NodeStatus.Waiting,
                DispatchMode = StepDispatchMode.Immediate,
                DependencyMode = dependencyMode,
                Dependencies = inputs.Select(input => input.Id).ToArray(),
                CreatedAt = now,
                UpdatedAt = now
            });
    }

    private IReadOnlyList<Node> ValidateBarrierInputs(IReadOnlyList<Node> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count == 0)
        {
            throw new ArgumentException("A wait barrier requires at least one input node.", nameof(nodes));
        }

        ValidateDependencies(nodes);
        if (nodes.Select(node => node.Id).Distinct().Count() != nodes.Count)
        {
            throw new ArgumentException("A wait barrier cannot contain duplicate input nodes.", nameof(nodes));
        }

        return Array.AsReadOnly(nodes.ToArray());
    }

    private void ValidateDependencies(IReadOnlyList<Node> dependencies)
    {
        foreach (var dependency in dependencies)
        {
            if (dependency is not IRuntimeNode runtimeNode || runtimeNode.FlowInstanceId != InstanceId)
            {
                throw new ArgumentException(
                    $"Node '{dependency.Id}' does not belong to flow instance '{InstanceId}'.",
                    nameof(dependencies));
            }
        }
    }

    private bool DependenciesSucceeded(IReadOnlyList<NodeId> dependencies)
        => dependencies.All(dependency =>
            session.TryGetNode(dependency, out var record) && record.Status == NodeStatus.Completed);

    private static Type GetDependencyResultType(Node dependency)
        => dependency is IRuntimeNode runtimeNode ? runtimeNode.ResultType : typeof(object);

    private RuntimeNodeState<TResult> CreateState<TResult>(NodeId id, string name, NodeKind kind)
        => new(store, session, serializer, InstanceId, id, name, kind);
}

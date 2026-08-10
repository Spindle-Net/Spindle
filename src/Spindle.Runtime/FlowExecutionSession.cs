using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Persistence.Nodes;

namespace Spindle;

internal sealed class FlowExecutionSession(FlowInstanceId flowInstanceId)
{
    private readonly Dictionary<NodeId, StepExecutionRegistration> _registrations = [];
    private readonly Dictionary<NodeId, NodeInstanceRecord> _nodes = [];
    private readonly Dictionary<NodeId, object?> _results = [];
    private readonly List<NodeId> _pendingNodeDeclarations = [];
    private readonly Dictionary<NodeId, NodeInitialization> _pendingInitializations = [];
    private readonly List<Task> _descriptorInitializationTasks = [];

    public FlowInstanceId FlowInstanceId { get; } = flowInstanceId;

    public void BeginReplay(
        IReadOnlyList<NodeInstanceRecord> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        _registrations.Clear();
        _nodes.Clear();
        _pendingNodeDeclarations.Clear();
        _pendingInitializations.Clear();
        lock (_results)
        {
            _results.Clear();
        }

        foreach (var node in nodes)
        {
            _nodes[node.NodeId] = node;
        }
    }

    public void Register<TResult>(
        NodeId nodeId,
        IReadOnlyList<Type> dependencyResultTypes,
        StepCallback<TResult> callback)
    {
        async ValueTask<object?> Execute(NodeInputs inputs, IStepExecutionContext context)
        {
            return await callback(inputs, context).ConfigureAwait(false);
        }

        _registrations[nodeId] = new StepExecutionRegistration(
            nodeId,
            typeof(TResult),
            dependencyResultTypes.ToArray(),
            Execute);
    }

    public bool TryGet(
        NodeId nodeId,
        out StepExecutionRegistration registration)
    {
        return _registrations.TryGetValue(nodeId, out registration!);
    }

    public bool TryGetNode(
        NodeId nodeId,
        out NodeInstanceRecord node)
    {
        return _nodes.TryGetValue(nodeId, out node!);
    }

    public bool TryGetResult(
        NodeId nodeId,
        out object? result)
    {
        lock (_results)
        {
            return _results.TryGetValue(nodeId, out result);
        }
    }

    public void SetResult(
        NodeId nodeId,
        object? result)
    {
        lock (_results)
        {
            _results[nodeId] = result;
        }
    }

    public IReadOnlyList<NodeInstanceRecord> GetNodesSnapshot()
    {
        return _nodes.Values
            .OrderBy(node => node.CreatedAt)
            .ThenBy(node => node.NodeId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryDeclareNode(
        NodeInstanceRecord node,
        NodeInitialization? initialization = null)
    {
        if (_nodes.ContainsKey(node.NodeId))
        {
            return false;
        }

        _nodes.Add(node.NodeId, node);
        _pendingNodeDeclarations.Add(node.NodeId);
        if (initialization is not null)
        {
            _pendingInitializations.Add(node.NodeId, initialization);
        }
        return true;
    }

    public void RegisterDescriptorAsyncInitialization(Task task)
    {
        _descriptorInitializationTasks.Add(task);
    }

    internal async Task WaitForAsyncDescriptorInitializationTasks()
    {
        foreach (var task in _descriptorInitializationTasks)
        {
            try
            {
                await task;
            } catch (FlowSuspendedException) { } // Ignore FlowSuspendedExceptions as they should only be propagated when needed
        }
    }

    public void UpsertNode(
        NodeInstanceRecord node)
    {
        _nodes[node.NodeId] = node;
    }

    public IReadOnlyList<NodeInstanceRecord> GetPendingNodeDeclarations()
    {
        return _pendingNodeDeclarations
            .Select(nodeId => _nodes[nodeId])
            .ToArray();
    }

    public IReadOnlyList<NodeInitialization> GetPendingNodeInitializations()
        => _pendingInitializations.Values.ToArray();

    public void MarkNodeDeclarationsFlushed()
    {
        _pendingNodeDeclarations.Clear();
        _pendingInitializations.Clear();
    }
}

using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Snapshot;
using Spindle.Persistence;

namespace Spindle;

internal sealed class RuntimeNodeState<TResult>(
    ISpindleStore store,
    FlowExecutionSession session,
    ISpindleSerializer serializer,
    FlowInstanceId flowInstanceId,
    NodeId id,
    string name,
    NodeKind kind)
{
    public ISpindleStore Store { get; } = store;

    public FlowExecutionSession Session { get; } = session;

    public ISpindleSerializer Serializer { get; } = serializer;

    public FlowInstanceId FlowInstanceId { get; } = flowInstanceId;

    public NodeId Id { get; } = id;

    public string Name { get; } = name;

    public NodeKind Kind { get; } = kind;

    public async ValueTask<TResult> GetResultAsync(CancellationToken cancellationToken)
    {
        if (!Session.TryGetNode(Id, out var record))
        {
            record = await Store.Nodes
                .GetAsync(FlowInstanceId, Id, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Node '{Id}' does not exist for flow instance '{FlowInstanceId}'.");
        }

        return record.Status switch
        {
            NodeStatus.Completed => record.Result is null
                ? default!
                : Serializer.Deserialize<TResult>(record.Result),
            NodeStatus.Failed => throw new InvalidOperationException(
                $"Node '{Id}' failed: {record.Error}"),
            NodeStatus.TimedOut => throw new TimeoutException(
                $"Node '{Id}' timed out: {record.Error}"),
            NodeStatus.Cancelled => throw new TaskCanceledException(),
            _ => throw new FlowSuspendedException()
        };
    }
}

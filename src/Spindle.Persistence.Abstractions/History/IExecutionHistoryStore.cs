using Spindle.Abstractions.Core;

namespace Spindle.Persistence.History;

public interface IExecutionHistoryStore
{
    ValueTask AppendAsync(
        ExecutionHistoryRecord record,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ExecutionHistoryRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default);
}

using Spindle.Abstractions.Core;
using Spindle.Persistence.History;

namespace Spindle.Persistence.InMemory.Stores;

public sealed class InMemoryExecutionHistoryStore : IExecutionHistoryStore
{
    private readonly object _gate = new();
    private readonly List<ExecutionHistoryRecord> _records = [];

    public ValueTask AppendAsync(
        ExecutionHistoryRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _records.Add(InMemoryRecordCopies.Copy(record));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ExecutionHistoryRecord>> GetByFlowInstanceAsync(
        FlowInstanceId flowInstanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var records = _records
                .Where(record => record.FlowInstanceId == flowInstanceId)
                .OrderBy(record => record.CreatedAt)
                .Select(InMemoryRecordCopies.Copy)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<ExecutionHistoryRecord>>(records);
        }
    }
}

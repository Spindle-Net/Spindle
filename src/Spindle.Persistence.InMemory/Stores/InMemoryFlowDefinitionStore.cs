using Spindle.Abstractions.Core;
using Spindle.Persistence.FlowDefinitions;

namespace Spindle.Persistence.InMemory.Stores;

public sealed class InMemoryFlowDefinitionStore : IFlowDefinitionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(FlowName FlowName, FlowVersion FlowVersion), FlowDefinitionRecord> _definitions = [];

    public ValueTask UpsertAsync(
        FlowDefinitionRecord definition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _definitions[(definition.FlowName, definition.FlowVersion)] =
                InMemoryRecordCopies.Copy(definition);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<FlowDefinitionRecord?> GetAsync(
        FlowName flowName,
        FlowVersion flowVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                _definitions.TryGetValue((flowName, flowVersion), out var definition)
                    ? InMemoryRecordCopies.Copy(definition)
                    : null);
        }
    }

    public ValueTask<IReadOnlyList<FlowDefinitionRecord>> GetByNameAsync(
        FlowName flowName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var result = _definitions.Values
                .Where(definition => definition.FlowName == flowName)
                .OrderBy(definition => definition.FlowVersion.Value, StringComparer.Ordinal)
                .Select(InMemoryRecordCopies.Copy)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<FlowDefinitionRecord>>(result);
        }
    }
}

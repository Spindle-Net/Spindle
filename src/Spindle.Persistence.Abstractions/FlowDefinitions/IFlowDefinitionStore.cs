using Spindle.Abstractions.Core;

namespace Spindle.Persistence.FlowDefinitions;

public interface IFlowDefinitionStore
{
    ValueTask UpsertAsync(
        FlowDefinitionRecord definition,
        CancellationToken cancellationToken = default);

    ValueTask<FlowDefinitionRecord?> GetAsync(
        FlowName flowName,
        FlowVersion flowVersion,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<FlowDefinitionRecord>> GetByNameAsync(
        FlowName flowName,
        CancellationToken cancellationToken = default);
}

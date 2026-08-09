using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;
using Spindle.Persistence.Nodes;

namespace Spindle;

internal interface IStepExecutor
{

    public bool SupportsDispatchMode(StepDispatchMode mode);

    public Task<StepExecutionResult> ExecuteAsync(
        FlowExecutionSession session,
        NodeInstanceRecord record,
        CancellationToken cancellationToken);

}

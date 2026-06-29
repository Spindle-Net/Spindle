using Spindle.Abstractions.Steps;
using Spindle.Persistence.Steps;

namespace Spindle;

internal interface IStepExecutor
{

    public bool SupportsDispatchMode(StepDispatchMode mode);

    public Task<bool> ExecuteAsync(
        FlowExecutionSession session,
        StepInstanceRecord record,
        CancellationToken cancellationToken);

}
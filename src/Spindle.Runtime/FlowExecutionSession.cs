using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence.Steps;

namespace Spindle;

internal sealed class FlowExecutionSession(FlowInstanceId flowInstanceId)
{
    private readonly Dictionary<StepId, StepExecutionRegistration> _registrations = [];
    private readonly Dictionary<StepId, StepInstanceRecord> _steps = [];
    private readonly List<StepId> _pendingStepDeclarations = [];

    public FlowInstanceId FlowInstanceId { get; } = flowInstanceId;

    public void BeginReplay(
        IReadOnlyList<StepInstanceRecord> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        _registrations.Clear();
        _steps.Clear();
        _pendingStepDeclarations.Clear();

        foreach (var step in steps)
        {
            _steps[step.StepId] = step;
        }
    }

    public void Register<TResult>(
        StepId stepId,
        IReadOnlyList<Type> dependencyResultTypes,
        StepCallback<TResult> callback)
    {
        async ValueTask<object?> Execute(StepInputs inputs, IStepExecutionContext context)
        {
            return await callback(inputs, context).ConfigureAwait(false);
        }

        _registrations[stepId] = new StepExecutionRegistration(
            stepId,
            typeof(TResult),
            dependencyResultTypes.ToArray(),
            Execute);
    }

    public bool TryGet(
        StepId stepId,
        out StepExecutionRegistration registration)
    {
        return _registrations.TryGetValue(stepId, out registration!);
    }

    public bool TryGetStep(
        StepId stepId,
        out StepInstanceRecord step)
    {
        return _steps.TryGetValue(stepId, out step!);
    }

    public IReadOnlyList<StepInstanceRecord> GetStepsSnapshot()
    {
        return _steps.Values
            .OrderBy(step => step.CreatedAt)
            .ThenBy(step => step.StepId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryDeclareStep(
        StepInstanceRecord step)
    {
        if (_steps.ContainsKey(step.StepId))
        {
            return false;
        }

        _steps.Add(step.StepId, step);
        _pendingStepDeclarations.Add(step.StepId);
        return true;
    }

    public void UpsertStep(
        StepInstanceRecord step)
    {
        _steps[step.StepId] = step;
    }

    public IReadOnlyList<StepInstanceRecord> GetPendingStepDeclarations()
    {
        return _pendingStepDeclarations
            .Select(stepId => _steps[stepId])
            .ToArray();
    }

    public void MarkStepDeclarationsFlushed()
    {
        _pendingStepDeclarations.Clear();
    }
}

internal sealed record StepExecutionRegistration(
    StepId StepId,
    Type ResultType,
    IReadOnlyList<Type> DependencyResultTypes,
    Func<StepInputs, IStepExecutionContext, ValueTask<object?>> Execute);

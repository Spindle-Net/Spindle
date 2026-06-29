using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence;

namespace Spindle;

internal interface IRuntimeStep
{
    Type ResultType { get; }
}

internal sealed class RuntimeStep<TResult>(
    ISpindleStore store,
    FlowExecutionSession session,
    ISpindleSerializer serializer,
    FlowInstanceId flowInstanceId,
    StepId id,
    string name,
    StepKind kind,
    StepOptions options)
    : Step<TResult>, IRuntimeStep
{
    public override StepId Id => id;

    public override string Name => name;

    public override StepKind Kind => kind;

    public override StepOptions Options => options;

    public Type ResultType => typeof(TResult);

    public override Step<TResult> WithOptions(
        Func<StepOptions, StepOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return new RuntimeStep<TResult>(
            store,
            session,
            serializer,
            flowInstanceId,
            id,
            name,
            kind,
            configure(options));
    }

    public override async ValueTask<TResult> GetResultAsync(
        CancellationToken cancellationToken = default)
    {
        if (!session.TryGetStep(id, out var record))
        {
            record = await store.Steps
                .GetAsync(flowInstanceId, id, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Step '{id}' does not exist for flow instance '{flowInstanceId}'.");
        }

        return record.Status switch
        {
            StepStatus.Completed => record.Result is null
                ? default!
                : serializer.Deserialize<TResult>(record.Result),
            StepStatus.Failed => throw new InvalidOperationException(
                $"Step '{id}' failed: {record.Error}"),
            _ => throw new FlowSuspendedException()
        };
    }
}

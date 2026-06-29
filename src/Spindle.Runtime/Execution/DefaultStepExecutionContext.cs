using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Steps;

namespace Spindle;

internal sealed class DefaultStepExecutionContext(
    FlowInstanceId flowInstanceId,
    StepId stepId,
    StepAttemptId attemptId,
    int attempt,
    IServiceProvider services,
    ILogger logger,
    CancellationToken cancellationToken)
    : IStepExecutionContext
{
    public FlowInstanceId FlowInstanceId => flowInstanceId;

    public StepId StepId => stepId;

    public StepAttemptId AttemptId => attemptId;

    public int Attempt => attempt;

    public CancellationToken CancellationToken => cancellationToken;

    public ILogger Logger => logger;

    public IServiceProvider Services => services;

    public ValueTask HeartbeatAsync<TProgress>(
        TProgress? progress = default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

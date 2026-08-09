using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle;

internal sealed class DefaultStepExecutionContext(
    FlowInstanceId flowInstanceId,
    NodeId nodeId,
    StepAttemptId attemptId,
    int attempt,
    IServiceProvider services,
    ILogger logger,
    CancellationToken cancellationToken)
    : IStepExecutionContext
{
    public FlowInstanceId FlowInstanceId => flowInstanceId;

    public NodeId NodeId => nodeId;

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

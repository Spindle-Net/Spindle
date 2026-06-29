using Microsoft.Extensions.Logging;
using Spindle.Abstractions.Core;

namespace Spindle.Abstractions.Steps;

public interface IStepExecutionContext
{
    FlowInstanceId FlowInstanceId { get; }

    StepId StepId { get; }

    StepAttemptId AttemptId { get; }

    int Attempt { get; }

    CancellationToken CancellationToken { get; }

    IServiceProvider Services { get; }

    ValueTask HeartbeatAsync<TProgress>(
        TProgress? progress = default,
        CancellationToken cancellationToken = default);

    ILogger Logger { get; }

}
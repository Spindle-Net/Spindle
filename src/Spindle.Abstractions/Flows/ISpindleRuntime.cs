using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Abstractions.Flows;

/// <summary>
/// The runtime component of Spindle.Net
/// </summary>
public interface ISpindleRuntime
{
    /// <summary>
    /// Triggers the start of a flow
    /// </summary>
    /// <param name="flowName">The name of the flow</param>
    /// <param name="request">The flow input/request</param>
    /// <param name="options">Flow options</param>
    /// <param name="cancellationToken">A cancellation token for queueing the flow</param>
    /// <typeparam name="TRequest">The type of the flow input data</typeparam>
    /// <typeparam name="TResult">The type that the flow responds with</typeparam>
    /// <returns>The flow handleSo</returns>
    ValueTask<FlowInstanceHandle<TResult>> StartAsync<TRequest, TResult>(
        FlowName flowName,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<FlowInstanceHandle<TResult>> StartAsync<TRequest, TResult>(
        FlowName flowName,
        FlowVersion flowVersion,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<TResult> RunAsync<TRequest, TResult>(
        FlowName flowName,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask SignalAsync<TSignal>(
        FlowInstanceId instanceId,
        SignalName signalName,
        TSignal payload,
        CancellationToken cancellationToken = default);

    ValueTask SignalAsync<TSignal>(
        SignalName signalName,
        CorrelationKey correlationKey,
        TSignal payload,
        CancellationToken cancellationToken = default);

    ValueTask CancelAsync(
        FlowInstanceId instanceId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    ValueTask RetryAsync(
        FlowInstanceId instanceId,
        StepId? stepId = null,
        CancellationToken cancellationToken = default);

    ValueTask<FlowInstanceSnapshot?> GetInstanceAsync(
        FlowInstanceId instanceId,
        CancellationToken cancellationToken = default);
}
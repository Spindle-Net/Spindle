using Spindle.Abstractions.Core;
using Spindle.Abstractions.Snapshot;

namespace Spindle.Abstractions.Flows;

/// <summary>
/// The runtime component of Spindle.Net
/// </summary>
public interface ISpindleRuntime
{
    /// <summary>
    /// Schedules a task to run on a Spindle Worker instance.
    /// </summary>
    /// <param name="flowName">The name of the flow</param>
    /// <param name="request">The flow input/request</param>
    /// <param name="options">Flow options</param>
    /// <param name="cancellationToken">A cancellation token for queueing the flow</param>
    /// <typeparam name="TRequest">The type of the flow input data</typeparam>
    /// <typeparam name="TResult">The type that the flow responds with</typeparam>
    /// <returns>The flow handle</returns>
    ValueTask<FlowInstanceHandle<TResult>> EnqueueAsync<TRequest, TResult>(
        FlowName flowName,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a task to run in the current runtime instance, will start immediately if possible. 
    /// </summary>
    /// <param name="flowName">The name of the flow</param>
    /// <param name="request">The flow input/request</param>
    /// <param name="options">Flow options</param>
    /// <param name="cancellationToken">A cancellation token for queueing the flow</param>
    /// <typeparam name="TRequest">The type of the flow input data</typeparam>
    /// <typeparam name="TResult">The type that the flow responds with</typeparam>
    /// <returns>The flow handle</returns>
    ValueTask<FlowInstanceHandle<TResult>> StartAsync<TRequest, TResult>(
        FlowName flowName,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a task to run on a Spindle Worker instance.
    /// </summary>
    /// <param name="flowName">The name of the flow</param>
    /// <param name="flowVersion">The version of the flow</param>
    /// <param name="request">The flow input/request</param>
    /// <param name="options">Flow options</param>
    /// <param name="cancellationToken">A cancellation token for queueing the flow</param>
    /// <typeparam name="TRequest">The type of the flow input data</typeparam>
    /// <typeparam name="TResult">The type that the flow responds with</typeparam>
    /// <returns>The flow handle</returns>
    ValueTask<FlowInstanceHandle<TResult>> EnqueueAsync<TRequest, TResult>(
        FlowName flowName,
        FlowVersion flowVersion,
        TRequest request,
        StartFlowOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a task to run in the current runtime instance, will start immediately if possible.
    /// </summary>
    /// <param name="flowName">The name of the flow</param>
    /// <param name="flowVersion">The version of the flow</param>
    /// <param name="request">The flow input/request</param>
    /// <param name="options">Flow options</param>
    /// <param name="cancellationToken">A cancellation token for queueing the flow</param>
    /// <typeparam name="TRequest">The type of the flow input data</typeparam>
    /// <typeparam name="TResult">The type that the flow responds with</typeparam>
    /// <returns>The flow handle</returns>
    /// <returns></returns>
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
        CorrelationKey correlationKey,
        TSignal payload,
        CancellationToken cancellationToken = default);

    ValueTask SignalAsync<TSignal>(
        SignalName signalName,
        CorrelationKey correlationKey,
        TSignal payload,
        CancellationToken cancellationToken = default);

    ValueTask SignalAsync(
        FlowInstanceId instanceId,
        SignalName signalName,
        CorrelationKey correlationKey,
        CancellationToken cancellationToken = default);

    ValueTask SignalAsync(
        SignalName signalName,
        CorrelationKey correlationKey,
        CancellationToken cancellationToken = default);

    ValueTask CancelAsync(
        FlowInstanceId instanceId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    ValueTask RetryAsync(
        FlowInstanceId instanceId,
        NodeId? nodeId = null,
        CancellationToken cancellationToken = default);

    ValueTask<FlowInstanceSnapshot?> GetInstanceAsync(
        FlowInstanceId instanceId,
        CancellationToken cancellationToken = default);
}
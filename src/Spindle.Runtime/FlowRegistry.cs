using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;

namespace Spindle;

public sealed class FlowRegistry
{
    public static FlowVersion DefaultVersion { get; } = new("1");

    private readonly object _gate = new();
    private readonly Dictionary<(FlowName FlowName, FlowVersion FlowVersion), FlowDescriptor> _flows = [];
    private readonly Dictionary<FlowName, FlowVersion> _latestVersions = [];

    public FlowDescriptor Register<TRequest, TResult>(
        FlowName flowName,
        ISpindleFlow<TRequest, TResult> flow,
        FlowVersion? flowVersion = null)
    {
        ArgumentNullException.ThrowIfNull(flow);

        var version = flowVersion ?? DefaultVersion;

        async ValueTask<object?> Execute(IFlowContext context, object? request)
        {
            return await flow.RunAsync(context, (TRequest)request!)
                .ConfigureAwait(false);
        }

        var descriptor = new FlowDescriptor(
            flowName,
            version,
            flow.GetType(),
            typeof(TRequest),
            typeof(TResult),
            Execute);

        lock (_gate)
        {
            _flows[(flowName, version)] = descriptor;
            _latestVersions[flowName] = version;
        }

        return descriptor;
    }

    public FlowDescriptor Register<TRequest, TResult>(
        FlowName flowName,
        Func<IFlowContext, TRequest, ValueTask<TResult>> run,
        FlowVersion? flowVersion = null)
    {
        ArgumentNullException.ThrowIfNull(run);

        return Register(flowName, new DelegateFlow<TRequest, TResult>(run), flowVersion);
    }

    public FlowDescriptor Resolve(
        FlowName flowName,
        FlowVersion? flowVersion = null)
    {
        lock (_gate)
        {
            var version = flowVersion ?? (_latestVersions.TryGetValue(flowName, out var latest)
                ? latest
                : DefaultVersion);

            if (_flows.TryGetValue((flowName, version), out var descriptor))
            {
                return descriptor;
            }
        }

        throw new InvalidOperationException(
            $"Flow '{flowName}' version '{flowVersion?.ToString() ?? "<latest>"}' is not registered.");
    }

    private sealed class DelegateFlow<TRequest, TResult>(
        Func<IFlowContext, TRequest, ValueTask<TResult>> run)
        : ISpindleFlow<TRequest, TResult>
    {
        public ValueTask<TResult> RunAsync(
            IFlowContext context,
            TRequest request)
        {
            return run(context, request);
        }
    }
}

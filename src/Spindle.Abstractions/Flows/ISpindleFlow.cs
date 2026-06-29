namespace Spindle.Abstractions.Flows;

public interface ISpindleFlow<in TRequest, TResult>
{
    ValueTask<TResult> RunAsync(
        IFlowContext context,
        TRequest request);
}
namespace Spindle.Abstractions.Steps;

public interface IStepHandler<in TRequest, TResult>
{
    ValueTask<TResult> ExecuteAsync(
        TRequest request,
        IStepExecutionContext context);
}
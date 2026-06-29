using Spindle.Abstractions.Core;
using Spindle.Abstractions.Steps;

namespace Spindle;

public sealed class StepHandlerRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<StepHandlerId, HandlerRegistration> _handlers = [];

    public void Register<THandler, TRequest, TResult>(
        StepHandlerId handlerId)
        where THandler : class, IStepHandler<TRequest, TResult>
    {
        lock (_gate)
        {
            _handlers[handlerId] = new HandlerRegistration(
                typeof(THandler),
                typeof(TRequest),
                typeof(TResult));
        }
    }

    public IStepHandler<TRequest, TResult>? Resolve<TRequest, TResult>(
        StepHandlerId handlerId,
        IServiceProvider services)
    {
        Type? handlerType;

        lock (_gate)
        {
            if (!_handlers.TryGetValue(handlerId, out var registration))
            {
                return null;
            }

            if (registration.RequestType != typeof(TRequest) ||
                registration.ResultType != typeof(TResult))
            {
                throw new InvalidOperationException(
                    $"Step handler '{handlerId}' was registered for '{registration.RequestType.Name}' -> '{registration.ResultType.Name}', not '{typeof(TRequest).Name}' -> '{typeof(TResult).Name}'.");
            }

            handlerType = registration.HandlerType;
        }

        return services.GetService(handlerType) as IStepHandler<TRequest, TResult>
            ?? Activator.CreateInstance(handlerType) as IStepHandler<TRequest, TResult>;
    }

    private sealed record HandlerRegistration(
        Type HandlerType,
        Type RequestType,
        Type ResultType);
}

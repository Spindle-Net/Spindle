using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Snapshot;
using Spindle.Abstractions.Steps;
using Spindle.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Spindle.Hosting;

public static class SpindleServiceCollectionExtensions
{
    public static IServiceCollection AddSpindleRuntime(
        this IServiceCollection services)
    {
        services.TryAddSingleton<FlowRegistry>();
        services.TryAddSingleton<StepHandlerRegistry>();
        services.TryAddSingleton<ISpindleSerializer, JsonSpindleSerializer>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        services.TryAddSingleton<RuntimeSpindleRuntime>(sp =>
        {
            var registry = sp.GetRequiredService<FlowRegistry>();
            foreach (var registration in sp.GetServices<IFlowRegistration>())
            {
                registration.Apply(sp, registry);
            }

            var stepHandlers = sp.GetRequiredService<StepHandlerRegistry>();
            foreach (var registration in sp.GetServices<IStepHandlerRegistration>())
            {
                registration.Apply(stepHandlers);
            }

            var hostOptions = sp.GetService<IOptions<SpindleHostOptions>>()?.Value
                ?? new SpindleHostOptions();

            return new RuntimeSpindleRuntime(
                sp.GetRequiredService<ISpindleStore>(),
                registry,
                new RuntimeSpindleOptions
                {
                    Serializer = sp.GetRequiredService<ISpindleSerializer>(),
                    TimeProvider = sp.GetRequiredService<TimeProvider>(),
                    Services = sp,
                    StepHandlers = stepHandlers,
                    WorkerId = hostOptions.WorkerId,
                    StepLeaseDuration = hostOptions.LeaseDuration
                });
        });

        services.TryAddSingleton<ISpindleRuntime>(sp =>
            sp.GetRequiredService<RuntimeSpindleRuntime>());

        return services;
    }

    public static IServiceCollection AddSpindleWorker(
        this IServiceCollection services,
        Action<SpindleHostOptions>? configure = null)
    {
        services.AddSpindleRuntime();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<ISpindleRuntimePump, SpindleRuntimePump>();
        services.AddHostedService<SpindleWorkerHostedService>();

        return services;
    }

    public static IServiceCollection AddSpindleFlow<TFlow, TRequest, TResult>(
        this IServiceCollection services,
        FlowName flowName,
        FlowVersion? flowVersion = null)
        where TFlow : class, ISpindleFlow<TRequest, TResult>
    {
        services.TryAddSingleton<TFlow>();
        services.AddSingleton<IFlowRegistration>(
            new FlowRegistration<TFlow, TRequest, TResult>(flowName, flowVersion));

        return services;
    }

    public static IServiceCollection AddSpindleStepHandler<THandler, TRequest, TResult>(
        this IServiceCollection services,
        StepHandlerId handlerId)
        where THandler : class, IStepHandler<TRequest, TResult>
    {
        services.TryAddTransient<THandler>();
        services.AddSingleton<IStepHandlerRegistration>(
            new StepHandlerRegistration<THandler, TRequest, TResult>(handlerId));

        return services;
    }

    private interface IFlowRegistration
    {
        void Apply(
            IServiceProvider services,
            FlowRegistry registry);
    }

    private sealed class FlowRegistration<TFlow, TRequest, TResult>(
        FlowName flowName,
        FlowVersion? flowVersion)
        : IFlowRegistration
        where TFlow : class, ISpindleFlow<TRequest, TResult>
    {
        public void Apply(
            IServiceProvider services,
            FlowRegistry registry)
        {
            registry.Register<TRequest, TResult>(
                flowName,
                services.GetRequiredService<TFlow>(),
                flowVersion);
        }
    }

    private interface IStepHandlerRegistration
    {
        void Apply(
            StepHandlerRegistry registry);
    }

    private sealed class StepHandlerRegistration<THandler, TRequest, TResult>(
        StepHandlerId handlerId)
        : IStepHandlerRegistration
        where THandler : class, IStepHandler<TRequest, TResult>
    {
        public void Apply(
            StepHandlerRegistry registry)
        {
            registry.Register<THandler, TRequest, TResult>(handlerId);
        }
    }
}

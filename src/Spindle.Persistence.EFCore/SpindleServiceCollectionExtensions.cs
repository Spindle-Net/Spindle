using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Spindle.Persistence.EFCore;

public static class SpindleServiceCollectionExtensions
{
    public static IServiceCollection AddSpindleEntityFramework(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddPooledDbContextFactory<SpindleDbContext>(options =>
        {
            options.ReplaceService<IModelCacheKeyFactory, SpindleModelCacheKeyFactory>();
            configure(options);
        });
        services.TryAddScoped<SpindleDbContext>(serviceProvider => serviceProvider
            .GetRequiredService<IDbContextFactory<SpindleDbContext>>()
            .CreateDbContext());
        services.TryAddSingleton<ISpindleStore, EFCoreSpindleStore>();

        return services;
    }
}

using Microsoft.EntityFrameworkCore;
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

        services.AddDbContextFactory<SpindleDbContext>(configure);
        services.TryAddSingleton<ISpindleStore, EFCoreSpindleStore>();

        return services;
    }
}

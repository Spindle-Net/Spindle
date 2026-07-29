using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.PostgreSQL;

public static class SpindlePostgreSqlServiceCollectionExtensions
{
    public static IServiceCollection AddSpindlePostgreSql(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddSpindleEntityFramework(options =>
            options.UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsAssembly(typeof(SpindlePostgreSqlServiceCollectionExtensions).Assembly.FullName)));
    }
}

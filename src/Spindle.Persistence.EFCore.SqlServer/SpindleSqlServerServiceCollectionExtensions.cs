using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.SqlServer;

public static class SpindleSqlServerServiceCollectionExtensions
{
    public static IServiceCollection AddSpindleSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddSpindleEntityFramework(options =>
        {
            options.UseSqlServer(
                connectionString,
                sqlServer => sqlServer
                    .MigrationsAssembly(typeof(SpindleSqlServerServiceCollectionExtensions).Assembly.FullName)
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromMilliseconds(100),
                        errorNumbersToAdd: null
                    ));
            options.EnableDetailedErrors()
                .EnableSensitiveDataLogging();
        });
    }
}

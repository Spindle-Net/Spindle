using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.Sqlite;

public static class SpindleSqliteServiceCollectionExtensions
{
    public static IServiceCollection AddSpindleSqlite(
        this IServiceCollection services,
        string connectionString)
    {
        return AddSpindleSqlite(services, connectionString, configure: null);
    }

    public static IServiceCollection AddSpindleSqlite(
        this IServiceCollection services,
        string connectionString,
        Action<DbContextOptionsBuilder>? configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddSpindleEntityFramework(options =>
        {
            options.UseSqlite(
                connectionString,
                sqlite => sqlite
                    .MigrationsAssembly(typeof(SpindleSqliteServiceCollectionExtensions).Assembly.FullName)
                    .ExecutionStrategy(dependencies => new SqliteRetryingExecutionStrategy(dependencies)));
            configure?.Invoke(options);
        });
    }

    public static IServiceCollection AddSpindleSqlite(
        this IServiceCollection services,
        SqliteConnection connection)
    {
        return AddSpindleSqlite(services, connection, configure: null);
    }

    public static IServiceCollection AddSpindleSqlite(
        this IServiceCollection services,
        SqliteConnection connection,
        Action<DbContextOptionsBuilder>? configure)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return services.AddSpindleEntityFramework(options =>
        {
            options.UseSqlite(
                connection,
                sqlite => sqlite
                    .MigrationsAssembly(typeof(SpindleSqliteServiceCollectionExtensions).Assembly.FullName)
                    .ExecutionStrategy(dependencies => new SqliteRetryingExecutionStrategy(dependencies)));
            configure?.Invoke(options);
        });
    }
}

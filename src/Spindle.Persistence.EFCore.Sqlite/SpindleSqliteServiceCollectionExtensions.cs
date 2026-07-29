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
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddSpindleEntityFramework(options =>
            options.UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(SpindleSqliteServiceCollectionExtensions).Assembly.FullName)));
    }

    public static IServiceCollection AddSpindleSqlite(
        this IServiceCollection services,
        SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return services.AddSpindleEntityFramework(options =>
            options.UseSqlite(
                connection,
                sqlite => sqlite.MigrationsAssembly(typeof(SpindleSqliteServiceCollectionExtensions).Assembly.FullName)));
    }
}

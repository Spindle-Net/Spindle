using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.SqlServer;

public static class SpindleSqlServerServiceCollectionExtensions
{
    public static IServiceCollection AddSpindleSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        return AddSpindleSqlServer(services, connectionString, schema: null, configure: null);
    }

    public static IServiceCollection AddSpindleSqlServer(
        this IServiceCollection services,
        string connectionString,
        Action<DbContextOptionsBuilder>? configure)
    {
        return AddSpindleSqlServer(services, connectionString, schema: null, configure);
    }

    public static IServiceCollection AddSpindleSqlServer(
        this IServiceCollection services,
        string connectionString,
        string? schema)
    {
        return AddSpindleSqlServer(services, connectionString, schema, configure: null);
    }

    public static IServiceCollection AddSpindleSqlServer(
        this IServiceCollection services,
        string connectionString,
        string? schema,
        Action<DbContextOptionsBuilder>? configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ValidateSchema(schema);

        return services.AddSpindleEntityFramework(options =>
        {
            options.UseSqlServer(
                connectionString,
                sqlServer => sqlServer
                    .MigrationsAssembly(typeof(SpindleSqlServerServiceCollectionExtensions).Assembly.FullName)
                    .MigrationsHistoryTable("__EFMigrationsHistory", schema)
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromMilliseconds(100),
                        errorNumbersToAdd: null
                    ));
            options.ReplaceService<IMigrationsSqlGenerator, SpindleSqlServerMigrationsSqlGenerator>();
            configure?.Invoke(options);
        });
    }

    private static void ValidateSchema(string? schema)
    {
        if (schema is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        }
    }
}

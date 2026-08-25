using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.PostgreSQL;

public static class SpindlePostgreSqlServiceCollectionExtensions
{
    public static IServiceCollection AddSpindlePostgreSql(
        this IServiceCollection services,
        string connectionString)
    {
        return AddSpindlePostgreSql(services, connectionString, schema: null, configure: null);
    }

    public static IServiceCollection AddSpindlePostgreSql(
        this IServiceCollection services,
        string connectionString,
        Action<DbContextOptionsBuilder>? configure)
    {
        return AddSpindlePostgreSql(services, connectionString, schema: null, configure);
    }

    public static IServiceCollection AddSpindlePostgreSql(
        this IServiceCollection services,
        string connectionString,
        string? schema)
    {
        return AddSpindlePostgreSql(services, connectionString, schema, configure: null);
    }

    public static IServiceCollection AddSpindlePostgreSql(
        this IServiceCollection services,
        string connectionString,
        string? schema,
        Action<DbContextOptionsBuilder>? configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ValidateSchema(schema);

        return services.AddSpindleEntityFramework(options =>
        {
            options.UseNpgsql(
                connectionString,
                postgres => postgres
                    .MigrationsAssembly(typeof(SpindlePostgreSqlServiceCollectionExtensions).Assembly.FullName)
                    .MigrationsHistoryTable("__EFMigrationsHistory", schema)
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromMilliseconds(100),
                        errorCodesToAdd: null));
            options.ReplaceService<IMigrationsSqlGenerator, SpindlePostgreSqlMigrationsSqlGenerator>();
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

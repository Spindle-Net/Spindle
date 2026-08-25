using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.PostgreSQL;

internal sealed class PostgreSqlDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<SpindleDbContext>
{
    public SpindleDbContext CreateDbContext(string[] args)
    {
        var schema = SpindleDesignTimeSchema.Get(args);

        var options = new DbContextOptionsBuilder<SpindleDbContext>()
            .UseNpgsql("Host=localhost;Database=spindle;Username=postgres;Password=postgres", postgres =>
                postgres
                    .MigrationsAssembly(typeof(PostgreSqlDesignTimeDbContextFactory).Assembly.FullName)
                    .MigrationsHistoryTable("__EFMigrationsHistory", schema)
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromMilliseconds(100),
                        errorCodesToAdd: null))
            .ReplaceService<IModelCacheKeyFactory, SpindleModelCacheKeyFactory>()
            .ReplaceService<IMigrationsSqlGenerator, SpindlePostgreSqlMigrationsSqlGenerator>()
            .Options;

        return new SpindleDbContext(options);
    }
}

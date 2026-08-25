using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.SqlServer;

internal sealed class SqlServerDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<SpindleDbContext>
{
    public SpindleDbContext CreateDbContext(string[] args)
    {
        var schema = SpindleDesignTimeSchema.Get(args);

        var options = new DbContextOptionsBuilder<SpindleDbContext>()
            .UseSqlServer("Server=localhost;Database=Spindle;User Id=sa;Password=Spindle1!;TrustServerCertificate=True", sqlServer =>
                sqlServer
                    .MigrationsAssembly(typeof(SqlServerDesignTimeDbContextFactory).Assembly.FullName)
                    .MigrationsHistoryTable("__EFMigrationsHistory", schema)
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromMilliseconds(100),
                        errorNumbersToAdd: null))
            .ReplaceService<IModelCacheKeyFactory, SpindleModelCacheKeyFactory>()
            .ReplaceService<IMigrationsSqlGenerator, SpindleSqlServerMigrationsSqlGenerator>()
            .Options;

        return new SpindleDbContext(options);
    }
}

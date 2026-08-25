using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.Sqlite;

internal sealed class SqliteDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<SpindleDbContext>
{
    public SpindleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpindleDbContext>()
            .UseSqlite("Data Source=spindle.db", sqlite =>
                sqlite
                    .MigrationsAssembly(typeof(SqliteDesignTimeDbContextFactory).Assembly.FullName)
                    .ExecutionStrategy(dependencies => new SqliteRetryingExecutionStrategy(dependencies)))
            .Options;

        return new SpindleDbContext(options);
    }
}

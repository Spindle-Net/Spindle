using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.PostgreSQL;

internal sealed class PostgreSqlDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<SpindleDbContext>
{
    public SpindleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpindleDbContext>()
            .UseNpgsql("Host=localhost;Database=spindle;Username=postgres;Password=postgres", postgres =>
                postgres.MigrationsAssembly(typeof(PostgreSqlDesignTimeDbContextFactory).Assembly.FullName))
            .Options;

        return new SpindleDbContext(options);
    }
}

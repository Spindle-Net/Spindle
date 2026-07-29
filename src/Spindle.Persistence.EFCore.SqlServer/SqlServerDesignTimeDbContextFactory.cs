using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.SqlServer;

internal sealed class SqlServerDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<SpindleDbContext>
{
    public SpindleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpindleDbContext>()
            .UseSqlServer("Server=localhost;Database=Spindle;User Id=sa;Password=Spindle1!;TrustServerCertificate=True", sqlServer =>
                sqlServer.MigrationsAssembly(typeof(SqlServerDesignTimeDbContextFactory).Assembly.FullName))
            .Options;

        return new SpindleDbContext(options);
    }
}

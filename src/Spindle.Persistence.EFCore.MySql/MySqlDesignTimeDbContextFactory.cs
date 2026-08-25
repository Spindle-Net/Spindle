using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.MySql;

internal sealed class MySqlDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<SpindleDbContext>
{
    public SpindleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpindleDbContext>()
            .UseMySQL("Server=localhost;Database=spindle;User=root;Password=spindle", mysql =>
                mysql
                    .MigrationsAssembly(typeof(MySqlDesignTimeDbContextFactory).Assembly.FullName)
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromMilliseconds(100),
                        errorNumbersToAdd: null))
            .Options;

        return new SpindleDbContext(options);
    }
}

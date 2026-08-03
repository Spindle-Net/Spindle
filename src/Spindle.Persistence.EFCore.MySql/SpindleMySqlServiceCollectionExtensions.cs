using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.MySql;

public static class SpindleMySqlServiceCollectionExtensions
{
    public static IServiceCollection AddSpindleMySql(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddSpindleEntityFramework(options =>
            options.UseMySQL(
                connectionString,
                mysql => mysql.MigrationsAssembly(typeof(SpindleMySqlServiceCollectionExtensions).Assembly.FullName)));
    }
}

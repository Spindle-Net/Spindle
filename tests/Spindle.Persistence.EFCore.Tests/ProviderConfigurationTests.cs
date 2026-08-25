using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Spindle.Persistence.EFCore;
using Spindle.Persistence.EFCore.MySql;
using Spindle.Persistence.EFCore.PostgreSQL;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Persistence.EFCore.SqlServer;
using Xunit;

namespace Spindle.Persistence.EFCore.Tests;

public sealed class ProviderConfigurationTests
{
    [Fact]
    public void ProviderConfigureActions_AreInvokedForEveryProvider()
    {
        var invoked = new bool[4];

        using (var provider = new ServiceCollection()
            .AddSpindleSqlite("Data Source=spindle-options.db", _ => invoked[0] = true)
            .BuildServiceProvider())
        {
            _ = provider.GetRequiredService<SpindleDbContext>();
        }

        using (var provider = new ServiceCollection()
            .AddSpindlePostgreSql("Host=localhost;Database=spindle", _ => invoked[1] = true)
            .BuildServiceProvider())
        {
            _ = provider.GetRequiredService<SpindleDbContext>();
        }

        using (var provider = new ServiceCollection()
            .AddSpindleMySql("Server=localhost;Database=spindle", _ => invoked[2] = true)
            .BuildServiceProvider())
        {
            _ = provider.GetRequiredService<SpindleDbContext>();
        }

        using (var provider = BuildSqlServerProvider(configure: _ => invoked[3] = true))
        {
            _ = provider.GetRequiredService<SpindleDbContext>();
        }

        Assert.All(invoked, Assert.True);
    }

    [Fact]
    public void SqlServerSensitiveDataLogging_IsOptInAndDetailedErrorsAreNotEnabledByDefault()
    {
        using var defaultProvider = BuildSqlServerProvider();
        using var defaultContext = defaultProvider.GetRequiredService<SpindleDbContext>();
        var defaultOptions = defaultContext.GetService<IDbContextOptions>();
        var defaultCoreOptions = defaultOptions.FindExtension<CoreOptionsExtension>()!;

        Assert.False(defaultCoreOptions.IsSensitiveDataLoggingEnabled);
        Assert.False(defaultCoreOptions.DetailedErrorsEnabled);

        using var configuredProvider = BuildSqlServerProvider(configure: options =>
            options.EnableSensitiveDataLogging());
        using var configuredContext = configuredProvider.GetRequiredService<SpindleDbContext>();
        var configuredCoreOptions = configuredContext
            .GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()!;

        Assert.True(configuredCoreOptions.IsSensitiveDataLoggingEnabled);
    }

    [Fact]
    public void RelationalProviders_EnableRetryOnFailure()
    {
        using var postgreSqlProvider = new ServiceCollection()
            .AddSpindlePostgreSql("Host=localhost;Database=spindle")
            .BuildServiceProvider();
        using var postgreSqlContext = postgreSqlProvider.GetRequiredService<SpindleDbContext>();

        using var mySqlProvider = new ServiceCollection()
            .AddSpindleMySql("Server=localhost;Database=spindle")
            .BuildServiceProvider();
        using var mySqlContext = mySqlProvider.GetRequiredService<SpindleDbContext>();

        using var sqlServerProvider = BuildSqlServerProvider();
        using var sqlServerContext = sqlServerProvider.GetRequiredService<SpindleDbContext>();

        using var sqliteProvider = new ServiceCollection()
            .AddSpindleSqlite("Data Source=spindle-options.db")
            .BuildServiceProvider();
        using var sqliteContext = sqliteProvider.GetRequiredService<SpindleDbContext>();

        Assert.True(postgreSqlContext.Database.CreateExecutionStrategy().RetriesOnFailure);
        Assert.True(mySqlContext.Database.CreateExecutionStrategy().RetriesOnFailure);
        Assert.True(sqlServerContext.Database.CreateExecutionStrategy().RetriesOnFailure);
        Assert.True(sqliteContext.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void SqlServerSchema_IsAppliedToModelAndMigrationsHistory()
    {
        using var provider = BuildSqlServerProvider(schema: "spindle");
        using var context = provider.GetRequiredService<SpindleDbContext>();

        AssertSchema(context, "spindle");
    }

    [Fact]
    public void PostgreSqlSchema_IsAppliedToModelAndMigrationsHistory()
    {
        using var provider = new ServiceCollection()
            .AddSpindlePostgreSql("Host=localhost;Database=spindle", schema: "spindle")
            .BuildServiceProvider();
        using var context = provider.GetRequiredService<SpindleDbContext>();

        AssertSchema(context, "spindle");
    }

    private static ServiceProvider BuildSqlServerProvider(
        string? schema = null,
        Action<DbContextOptionsBuilder>? configure = null)
    {
        return new ServiceCollection()
            .AddSpindleSqlServer(
                "Server=localhost;Database=spindle",
                schema,
                configure)
            .BuildServiceProvider();
    }

    private static void AssertSchema(SpindleDbContext context, string schema)
    {
        Assert.Equal(schema, context.Model.GetDefaultSchema());

        var tableEntityTypes = context.Model
            .GetEntityTypes()
            .Where(entityType => entityType.GetTableName() is not null)
            .ToList();
        Assert.NotEmpty(tableEntityTypes);
        Assert.All(tableEntityTypes, entityType => Assert.Equal(schema, entityType.GetSchema()));

        var relationalOptions = RelationalOptionsExtension.Extract(
            context.GetService<IDbContextOptions>());
        Assert.Equal(schema, relationalOptions.MigrationsHistoryTableSchema);
    }
}

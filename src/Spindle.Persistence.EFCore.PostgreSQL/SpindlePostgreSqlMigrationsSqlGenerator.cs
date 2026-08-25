using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;
using Spindle.Persistence.EFCore;

#pragma warning disable EF1001

namespace Spindle.Persistence.EFCore.PostgreSQL;

internal sealed class SpindlePostgreSqlMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    INpgsqlSingletonOptions npgsqlSingletonOptions,
    ICurrentDbContext currentContext)
    : NpgsqlMigrationsSqlGenerator(dependencies, npgsqlSingletonOptions)
{
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model,
        MigrationsSqlGenerationOptions options)
    {
        var schema = currentContext.Context is SpindleDbContext spindleContext
            ? spindleContext.Schema
            : null;
        return base.Generate(
            SchemaAwareMigrationOperations.Prepare(operations, model, schema),
            model,
            options);
    }

    protected override void Generate(
        EnsureSchemaOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder
            .Append("CREATE SCHEMA IF NOT EXISTS ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(Dependencies.SqlGenerationHelper.StatementTerminator)
            .EndCommand();
    }
}

#pragma warning restore EF1001

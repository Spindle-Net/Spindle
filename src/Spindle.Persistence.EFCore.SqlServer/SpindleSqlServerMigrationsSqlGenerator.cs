using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Update;
using Spindle.Persistence.EFCore;

namespace Spindle.Persistence.EFCore.SqlServer;

internal sealed class SpindleSqlServerMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    ICommandBatchPreparer commandBatchPreparer,
    ICurrentDbContext currentContext)
    : SqlServerMigrationsSqlGenerator(dependencies, commandBatchPreparer)
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
}

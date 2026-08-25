using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Spindle.Persistence.EFCore;

internal static class SchemaAwareMigrationOperations
{
    public static IReadOnlyList<MigrationOperation> Prepare(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model,
        string? configuredSchema)
    {
        var schema = model is null
            ? configuredSchema
            : ((IReadOnlyModel)model).GetDefaultSchema() ?? configuredSchema;
        if (string.IsNullOrWhiteSpace(schema))
        {
            return operations;
        }

        var prepared = new List<MigrationOperation>(operations.Count + 1);
        if (!operations.OfType<EnsureSchemaOperation>().Any(operation => operation.Name == schema))
        {
            prepared.Add(new EnsureSchemaOperation { Name = schema });
        }

        foreach (var operation in operations)
        {
            ApplySchema(operation, schema);
            prepared.Add(operation);
        }

        return prepared;
    }

    private static void ApplySchema(MigrationOperation operation, string schema)
    {
        switch (operation)
        {
            case AddCheckConstraintOperation addCheckConstraint:
                addCheckConstraint.Schema ??= schema;
                break;
            case AddColumnOperation addColumn:
                addColumn.Schema ??= schema;
                break;
            case AddForeignKeyOperation addForeignKey:
                addForeignKey.Schema ??= schema;
                addForeignKey.PrincipalSchema ??= schema;
                break;
            case AddPrimaryKeyOperation addPrimaryKey:
                addPrimaryKey.Schema ??= schema;
                break;
            case AddUniqueConstraintOperation addUniqueConstraint:
                addUniqueConstraint.Schema ??= schema;
                break;
            case AlterColumnOperation alterColumn:
                alterColumn.Schema ??= schema;
                break;
            case AlterSequenceOperation alterSequence:
                alterSequence.Schema ??= schema;
                break;
            case AlterTableOperation alterTable:
                alterTable.Schema ??= schema;
                break;
            case CreateIndexOperation createIndex:
                createIndex.Schema ??= schema;
                break;
            case CreateTableOperation createTable:
                createTable.Schema ??= schema;
                if (createTable.PrimaryKey is not null)
                {
                    ApplySchema(createTable.PrimaryKey, schema);
                }
                foreach (var column in createTable.Columns)
                {
                    column.Schema ??= schema;
                }

                foreach (var foreignKey in createTable.ForeignKeys)
                {
                    ApplySchema(foreignKey, schema);
                }

                foreach (var uniqueConstraint in createTable.UniqueConstraints)
                {
                    ApplySchema(uniqueConstraint, schema);
                }

                foreach (var checkConstraint in createTable.CheckConstraints)
                {
                    ApplySchema(checkConstraint, schema);
                }

                break;
            case DeleteDataOperation deleteData:
                deleteData.Schema ??= schema;
                break;
            case DropCheckConstraintOperation dropCheckConstraint:
                dropCheckConstraint.Schema ??= schema;
                break;
            case DropColumnOperation dropColumn:
                dropColumn.Schema ??= schema;
                break;
            case DropForeignKeyOperation dropForeignKey:
                dropForeignKey.Schema ??= schema;
                break;
            case DropIndexOperation dropIndex:
                dropIndex.Schema ??= schema;
                break;
            case DropPrimaryKeyOperation dropPrimaryKey:
                dropPrimaryKey.Schema ??= schema;
                break;
            case DropSequenceOperation dropSequence:
                dropSequence.Schema ??= schema;
                break;
            case DropTableOperation dropTable:
                dropTable.Schema ??= schema;
                break;
            case DropUniqueConstraintOperation dropUniqueConstraint:
                dropUniqueConstraint.Schema ??= schema;
                break;
            case InsertDataOperation insertData:
                insertData.Schema ??= schema;
                break;
            case RenameColumnOperation renameColumn:
                renameColumn.Schema ??= schema;
                break;
            case RenameIndexOperation renameIndex:
                renameIndex.Schema ??= schema;
                break;
            case RenameSequenceOperation renameSequence:
                renameSequence.Schema ??= schema;
                renameSequence.NewSchema ??= schema;
                break;
            case RenameTableOperation renameTable:
                renameTable.Schema ??= schema;
                renameTable.NewSchema ??= schema;
                break;
            case UpdateDataOperation updateData:
                updateData.Schema ??= schema;
                break;
        }
    }
}

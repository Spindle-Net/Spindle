using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Storage;

namespace Spindle.Persistence.EFCore.Sqlite;

internal sealed class SqliteRetryingExecutionStrategy(
    ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, maxRetryCount: 5, maxRetryDelay: TimeSpan.FromMilliseconds(100))
{
    protected override bool ShouldRetryOn(Exception exception)
    {
        return exception is SqliteException
            {
                SqliteErrorCode: 5 or 6,
            };
    }
}

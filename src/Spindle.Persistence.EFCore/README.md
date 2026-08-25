# Spindle.Persistence.EFCore

Entity Framework Core persistence for Spindle.Net. The relational model is shared by four provider packages, each of which contains its own migrations.

Install the provider package for your database, then register it before the Spindle runtime:

```csharp
services.AddSpindleSqlite("Data Source=spindle.db");

services.AddSpindleWorker();
```

The available registrations are:

```csharp
services.AddSpindleSqlite(sqliteConnectionString);
services.AddSpindlePostgreSql(postgreSqlConnectionString);
services.AddSpindleMySql(mySqlConnectionString);
services.AddSpindleSqlServer(sqlServerConnectionString);
```

All registrations also accept an optional `Action<DbContextOptionsBuilder>` that
runs after Spindle's provider defaults. Use it for application-specific EF Core
options, such as `EnableSensitiveDataLogging()` when that is appropriate for the
environment. SQL Server sensitive-data logging is disabled by default.

PostgreSQL and SQL Server additionally accept an optional schema. The schema is
applied to Spindle's entity tables and migrations history table, and is created
by generated migration SQL when needed:

```csharp
services.AddSpindlePostgreSql(
    postgreSqlConnectionString,
    schema: "spindle",
    configure: options => options.EnableDetailedErrors());
```

MySQL and SQLite do not expose schema parameters because their installed EF Core
providers do not support EF Core schemas.

Create or migrate the schema during application startup using the normal EF Core APIs:

```csharp
await using var scope = services.CreateAsyncScope();
var contextFactory = scope.ServiceProvider
    .GetRequiredService<IDbContextFactory<SpindleDbContext>>();
await using var database = await contextFactory.CreateDbContextAsync();
await database.Database.MigrateAsync();
```

## Managing migrations

The repository includes equivalent Bash and PowerShell helpers that run `dotnet ef` against the provider-specific projects:

```bash
./scripts/migrations.sh add all AddFlowPriority
./scripts/migrations.sh check all
./scripts/migrations.sh list sqlite
./scripts/migrations.sh script postgresql 0
```

```powershell
./scripts/migrations.ps1 add all AddFlowPriority
./scripts/migrations.ps1 check all
./scripts/migrations.ps1 list sqlite
./scripts/migrations.ps1 script postgresql 0
```

Supported provider names are `sqlite`, `postgresql`, `mysql`, and `sqlserver`; `all` runs the command for every provider. Use `remove <provider> --force` in Bash or `remove <provider> -Force` in PowerShell only when a migration can be removed without checking a database. The `script` command accepts optional `from-migration` and `to-migration` arguments and writes SQL to standard output. Both scripts use `net10.0` for design-time operations by default; set `SPINDLE_EF_FRAMEWORK` to override it. The `add`, `check`, `list`, and `remove` commands always use schema-neutral design-time models. To validate a PostgreSQL or SQL Server migration script for a non-default schema, set `SPINDLE_EF_SCHEMA` before using the `script` command. The marker and value are ignored by the MySQL and SQLite design-time factories.

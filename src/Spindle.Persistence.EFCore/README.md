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
```

```powershell
./scripts/migrations.ps1 add all AddFlowPriority
./scripts/migrations.ps1 check all
./scripts/migrations.ps1 list sqlite
```

Supported provider names are `sqlite`, `postgresql`, `mysql`, and `sqlserver`; `all` runs the command for every provider. Use `remove <provider> --force` in Bash or `remove <provider> -Force` in PowerShell only when a migration can be removed without checking a database. Both scripts use `net10.0` for design-time operations by default; set `SPINDLE_EF_FRAMEWORK` to override it.

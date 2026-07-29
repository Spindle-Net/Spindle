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

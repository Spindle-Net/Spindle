using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spindle.Example.Hosting;
using Spindle.Hosting;
using Spindle.Persistence;
using Spindle.Persistence.EFCore;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Persistence.InMemory;
using System.Threading.Channels;

var textInbox = Channel.CreateUnbounded<string>();
foreach (var text in new[]
{
    "Hello Durable Workflows",
    "Spindle hosted services",
    "Long running flow worker"
})
{
    textInbox.Writer.TryWrite(text);
}

textInbox.Writer.Complete();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ChannelReader<string>>(textInbox.Reader);
//builder.Services.AddSingleton<ISpindleStore, InMemorySpindleStore>();
var sqliteConnectionString =
    "Data Source=SpindleHostingExample;Mode=Memory;Cache=Shared";
await using var sqliteDatabaseAnchor = new SqliteConnection(sqliteConnectionString);
await sqliteDatabaseAnchor.OpenAsync();
builder.Services.AddSpindleSqlite(sqliteConnectionString);

builder.Services.AddSpindleFlow<TextTransformFlow, TextTransformRequest, TextTransformResult>(
    TextTransformFlow.Name);

builder.Services.AddSpindleWorker(options =>
{
    options.PollInterval = TimeSpan.FromMilliseconds(100);
    options.MaxConcurrentFlowInstances = 4;
    options.MaxFlowInstancesPerTick = 16;
    options.MaxStepsPerFlowPerTick = 1;
    options.WorkerId = "hosting-example-worker";
});

builder.Services.AddHostedService<TextConsumerService>();

var app = builder.Build();

await using var scope = app.Services.CreateAsyncScope();
var contextFactory = scope.ServiceProvider
    .GetRequiredService<IDbContextFactory<SpindleDbContext>>();
await using var database = await contextFactory.CreateDbContextAsync();
await database.Database.MigrateAsync();

await app.RunAsync();

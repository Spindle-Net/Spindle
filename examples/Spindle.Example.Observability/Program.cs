using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Spindle.Abstractions.Core;
using Spindle.Example.Hosting;
using Spindle.Example.Observability;
using Spindle.Hosting;
using Spindle.Persistence.EFCore;
using Spindle.Persistence.EFCore.Sqlite;
using Spindle.Persistence.EFCore.SqlServer;
using System.Diagnostics;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

var activitySource = new ActivitySource("Spindle.Example.Observability");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: builder.Environment.ApplicationName))
    .WithTracing(tracing =>
    {
        tracing.AddEntityFrameworkCoreInstrumentation(options =>
        {
            options.EnrichWithIDbCommand = (activity, command) =>
            {
                activity.DisplayName = command.CommandText[..Math.Min(command.CommandText.Length, 50)];
            };
        });
        tracing.AddSource(Spindle.Runtime.Telemetry.ActivitySourceName);
        tracing.AddSource(SpindleEFCoreTelemetry.ActivitySourceName);
        tracing.AddSource(activitySource.Name);
        tracing.AddOtlpExporter(c =>
        {
            c.Endpoint = new Uri(Environment.GetEnvironmentVariable("OTEL_COLLECTOR_URL") ?? "http://otel-collector:4318/v1/traces");
            c.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
        });
    });
//builder.Services.AddSingleton<ISpindleStore, InMemorySpindleStore>();
var sqliteConnectionString =
    "Data Source=SpindleHostingExample;Mode=Memory;Cache=Shared";
await using var sqliteDatabaseAnchor = new SqliteConnection(sqliteConnectionString);
await sqliteDatabaseAnchor.OpenAsync();
builder.Services.AddSpindleSqlite(sqliteConnectionString);

builder.Services.AddSpindleFlow<UnitDummyFlow, Unit, Unit>(UnitDummyFlow.Name);
builder.Services.AddSpindleRuntime();
builder.Services.AddSpindleWorker(options =>
{
    options.PollInterval = TimeSpan.FromMilliseconds(100);
    options.MaxConcurrentFlowInstances = 4;
    options.MaxFlowInstancesPerTick = 16;
    options.MaxStepsPerFlowPerTick = 128;
    options.WorkerId = "observability-example-worker";
});

builder.Services.AddHostedService<FlowControlService>();

var app = builder.Build();

await using var scope = app.Services.CreateAsyncScope();
var contextFactory = scope.ServiceProvider
    .GetRequiredService<IDbContextFactory<SpindleDbContext>>();
await using var database = await contextFactory.CreateDbContextAsync();
await database.Database.MigrateAsync();

await app.RunAsync();

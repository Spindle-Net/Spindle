using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spindle.Abstractions.Core;
using Spindle.Example.Hosting;
using Spindle.Hosting;
using Spindle.Persistence;
using Spindle.Persistence.InMemory;

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
builder.Services.AddSingleton<ISpindleStore, InMemorySpindleStore>();

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

await builder.Build().RunAsync();

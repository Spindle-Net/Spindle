using Spindle;
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Example.LowLevel;
using Spindle.Persistence.InMemory;

var store = new InMemorySpindleStore();
var runtime = new RuntimeSpindleRuntime(store);
var flowName = new FlowName("low-level-example");

runtime.RegisterFlow(
    flowName,
    new LowLevelFlow());

var input = new LowLevelFlowInput("Hello Durable Workflows");
var result = await runtime.RunAsync<LowLevelFlowInput, LowLevelFlowOutput>(
    flowName,
    input,
    new StartFlowOptions { IdempotencyKey = "low-level-example" });

Console.WriteLine($"Input:     {input.Str}");
Console.WriteLine($"Upper:     {result.Upper}");
Console.WriteLine($"Lower:     {result.Lower}");
Console.WriteLine($"CamelCase: {result.CamelCase}");

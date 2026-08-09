using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 2, warmupCount: 3, iterationCount: 12, invocationCount: 1)]
public class ConcurrentFlowBenchmarks : WorkflowBenchmarkBase
{
    [ParamsSource(nameof(TaskCountValues))]
    public int TaskCount { get; set; }

    [ParamsSource(nameof(ConcurrentFlowCountValues))]
    public int ConcurrentFlowCount { get; set; }

    public IEnumerable<int> TaskCountValues => BenchmarkProfile.TaskCounts;

    public IEnumerable<int> ConcurrentFlowCountValues => BenchmarkProfile.ConcurrentFlowCounts;

    [IterationSetup]
    public void Setup() => SetupAsync(CreateConcurrentTaskFlow(TaskCount)).GetAwaiter().GetResult();

    [IterationCleanup]
    public void Cleanup() => CleanupAsync().GetAwaiter().GetResult();

    [Benchmark(Description = "Full flow: concurrent tasks")]
    public Task RunConcurrentFlows() => RunFlowsAsync(ConcurrentFlowCount);

    private static Func<IFlowContext, int, ValueTask<int>> CreateConcurrentTaskFlow(int taskCount)
    {
        return async (context, _) =>
        {
            var tasks = Enumerable.Range(0, taskCount)
                .Select(index => context.Step<int>($"task-{index:D4}", "Task", [], static (_, _) => ValueTask.FromResult(1)))
                .ToArray();

            await context.WaitAll("wait-all", "Wait for all", [.. tasks]);
            return tasks.Length;
        };
    }
}

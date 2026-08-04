using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

namespace Spindle.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 2, warmupCount: 3, iterationCount: 12, invocationCount: 1)]
public class ExistingStoreBenchmarks : WorkflowBenchmarkBase
{
    private const int StepCount = 8;

    [ParamsSource(nameof(PreloadedFlowCountValues))]
    public int PreloadedFlowCount { get; set; }

    public IEnumerable<int> PreloadedFlowCountValues => BenchmarkProfile.PreloadedFlowCounts;

    [IterationSetup]
    public void Setup()
    {
        SetupAsync(BenchmarkFlows.CreateSequential(StepCount)).GetAwaiter().GetResult();
        SeedCompletedFlowsAsync(PreloadedFlowCount, StepCount).GetAwaiter().GetResult();
    }

    [IterationCleanup]
    public void Cleanup() => CleanupAsync().GetAwaiter().GetResult();

    [Benchmark(Description = "Full flow: preloaded store")]
    public Task RunFlowAgainstPreloadedStore() => RunFlowsAsync(1);
}

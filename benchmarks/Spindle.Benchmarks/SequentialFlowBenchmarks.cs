using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

namespace Spindle.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 2, warmupCount: 3, iterationCount: 12, invocationCount: 1)]
public class SequentialFlowBenchmarks : WorkflowBenchmarkBase
{
    [ParamsSource(nameof(SequentialStepCountValues))]
    public int StepCount { get; set; }

    [ParamsSource(nameof(ConcurrentFlowCountValues))]
    public int ConcurrentFlowCount { get; set; }

    public IEnumerable<int> SequentialStepCountValues => BenchmarkProfile.SequentialStepCounts;

    public IEnumerable<int> ConcurrentFlowCountValues => BenchmarkProfile.SequentialFlowCounts;

    [IterationSetup]
    public void Setup() => SetupAsync(BenchmarkFlows.CreateSequential(StepCount)).GetAwaiter().GetResult();

    [IterationCleanup]
    public void Cleanup() => CleanupAsync().GetAwaiter().GetResult();

    [Benchmark(Description = "Full flow: sequential chain")]
    public Task RunSequentialFlows() => RunFlowsAsync(ConcurrentFlowCount);

}

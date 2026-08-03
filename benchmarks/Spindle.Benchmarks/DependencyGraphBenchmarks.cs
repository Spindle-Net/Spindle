using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

namespace Spindle.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 2, warmupCount: 3, iterationCount: 12, invocationCount: 1)]
public class DependencyGraphBenchmarks : WorkflowBenchmarkBase
{
    [Params(DependencyGraphShape.FanOut, DependencyGraphShape.FanIn, DependencyGraphShape.Diamond)]
    public DependencyGraphShape Shape { get; set; }

    [ParamsSource(nameof(WidthValues))]
    public int Width { get; set; }

    [ParamsSource(nameof(ConcurrentFlowCountValues))]
    public int ConcurrentFlowCount { get; set; }

    public IEnumerable<int> WidthValues => BenchmarkProfile.DependencyWidths;

    public IEnumerable<int> ConcurrentFlowCountValues => BenchmarkProfile.DependencyFlowCounts;

    [IterationSetup]
    public void Setup() => SetupAsync(BenchmarkFlows.CreateDependencyGraph(Shape, Width)).GetAwaiter().GetResult();

    [IterationCleanup]
    public void Cleanup() => CleanupAsync().GetAwaiter().GetResult();

    [Benchmark(Description = "Full flow: dependency graph")]
    public Task RunDependencyGraphFlows() => RunFlowsAsync(ConcurrentFlowCount);
}

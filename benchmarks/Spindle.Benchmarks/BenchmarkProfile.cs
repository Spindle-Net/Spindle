namespace Spindle.Benchmarks;

internal static class BenchmarkProfile
{
    public const string ProfileEnvironmentVariable = "SPINDLE_BENCHMARK_PROFILE";

    private static readonly int[] FullSequentialStepCounts = [8, 16, 32, 64, 128, 256, 384, 512, 768, 1_024];
    private static readonly int[] QuickSequentialStepCounts = [8, 64, 256, 768, 1_024];
    private static readonly int[] FullSequentialFlowCounts = [1, 2, 3, 4];
    private static readonly int[] QuickSequentialFlowCounts = [1, 4];
    private static readonly int[] FullTaskCounts = [1, 2, 4, 8, 16];
    private static readonly int[] QuickTaskCounts = [1, 4, 16];
    private static readonly int[] FullConcurrentFlowCounts = [1, 2, 4, 8, 16, 32, 64];
    private static readonly int[] QuickConcurrentFlowCounts = [1, 8, 64];
    private static readonly int[] FullDependencyWidths = [4, 16, 64];
    private static readonly int[] QuickDependencyWidths = [4, 16];
    private static readonly int[] FullDependencyFlowCounts = [1, 4, 16];
    private static readonly int[] QuickDependencyFlowCounts = [1, 4];
    private static readonly int[] FullPreloadedFlowCounts = [0, 100, 1_000];
    private static readonly int[] QuickPreloadedFlowCounts = [0, 1_000];

    public static IEnumerable<int> SequentialStepCounts => IsFullRun ? FullSequentialStepCounts : QuickSequentialStepCounts;

    public static IEnumerable<int> SequentialFlowCounts => IsFullRun ? FullSequentialFlowCounts : QuickSequentialFlowCounts;

    public static IEnumerable<int> TaskCounts => IsFullRun ? FullTaskCounts : QuickTaskCounts;

    public static IEnumerable<int> ConcurrentFlowCounts => IsFullRun ? FullConcurrentFlowCounts : QuickConcurrentFlowCounts;

    public static IEnumerable<int> DependencyWidths => IsFullRun ? FullDependencyWidths : QuickDependencyWidths;

    public static IEnumerable<int> DependencyFlowCounts => IsFullRun ? FullDependencyFlowCounts : QuickDependencyFlowCounts;

    public static IEnumerable<int> PreloadedFlowCounts => IsFullRun ? FullPreloadedFlowCounts : QuickPreloadedFlowCounts;

    private static bool IsFullRun => string.Equals(
        Environment.GetEnvironmentVariable(ProfileEnvironmentVariable),
        "full",
        StringComparison.OrdinalIgnoreCase);
}

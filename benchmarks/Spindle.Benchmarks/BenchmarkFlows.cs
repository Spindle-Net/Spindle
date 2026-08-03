using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Steps;

namespace Spindle.Benchmarks;

internal static class BenchmarkFlows
{
    public static Func<IFlowContext, int, ValueTask<int>> CreateSequential(int stepCount)
    {
        return async (context, _) =>
        {
            Step<int> current = AddStep(context, "step-0000", []);
            for (var index = 1; index < stepCount; index++)
            {
                current = AddStep(context, $"step-{index:D4}", [current]);
            }

            await context.WaitAll(current);
            return stepCount;
        };
    }

    public static Func<IFlowContext, int, ValueTask<int>> CreateDependencyGraph(
        DependencyGraphShape shape,
        int width)
    {
        return shape switch
        {
            DependencyGraphShape.FanOut => CreateFanOut(width),
            DependencyGraphShape.FanIn => CreateFanIn(width),
            DependencyGraphShape.Diamond => CreateDiamond(width),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown dependency graph shape."),
        };
    }

    private static Func<IFlowContext, int, ValueTask<int>> CreateFanOut(int width)
    {
        return async (context, _) =>
        {
            var root = AddStep(context, "root", []);
            var leaves = Enumerable.Range(0, width)
                .Select(index => AddStep(context, $"leaf-{index:D4}", [root]))
                .ToArray();

            await context.WaitAll([.. leaves]);
            return leaves.Length + 1;
        };
    }

    private static Func<IFlowContext, int, ValueTask<int>> CreateFanIn(int width)
    {
        return async (context, _) =>
        {
            var roots = Enumerable.Range(0, width)
                .Select(index => AddStep(context, $"root-{index:D4}", []))
                .ToArray();
            var join = AddStep(context, "join", [.. roots]);

            await context.WaitAll(join);
            return roots.Length + 1;
        };
    }

    private static Func<IFlowContext, int, ValueTask<int>> CreateDiamond(int width)
    {
        return async (context, _) =>
        {
            var root = AddStep(context, "root", []);
            var middle = Enumerable.Range(0, width)
                .Select(index => AddStep(context, $"middle-{index:D4}", [root]))
                .ToArray();
            var join = AddStep(context, "join", [.. middle]);

            await context.WaitAll(join);
            return middle.Length + 2;
        };
    }

    private static Step<int> AddStep(IFlowContext context, string id, IReadOnlyList<Step> dependencies)
    {
        return context.Step<int>(id, "Step", dependencies, static (_, _) => ValueTask.FromResult(1));
    }
}

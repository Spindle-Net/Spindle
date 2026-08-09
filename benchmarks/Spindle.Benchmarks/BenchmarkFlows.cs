using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Nodes;
using Spindle.Abstractions.Steps;

namespace Spindle.Benchmarks;

internal static class BenchmarkFlows
{
    public static Func<IFlowContext, int, ValueTask<int>> CreateSequential(int stepCount)
    {
        return async (context, _) =>
        {
            StepNode<int> current = AddStep(context, "step-0000", []);
            for (var index = 1; index < stepCount; index++)
            {
                current = AddStep(context, $"step-{index:D4}", [current]);
            }

            await context.WaitAll("wait-all", "Wait for all", current);
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

            await context.WaitAll("wait-all", "Wait for all", [.. leaves]);
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

            await context.WaitAll("wait-all", "Wait for all", join);
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

            await context.WaitAll("wait-all", "Wait for all", join);
            return middle.Length + 2;
        };
    }

    private static StepNode<int> AddStep(IFlowContext context, string id, IReadOnlyList<Node> dependencies)
    {
        return context.Step<int>(id, "Step", dependencies, static (_, _) => ValueTask.FromResult(1));
    }
}

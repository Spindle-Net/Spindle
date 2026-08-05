using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;

namespace Spindle.Example.Observability;

public sealed class UnitDummyFlow : ISpindleFlow<Unit, Unit>
{
    public static FlowName Name { get; } = new("unit-dummy-flow");

    public async ValueTask<Unit> RunAsync(IFlowContext ctx, Unit _)
    {
        var dummy = ctx.Step<int>(
            id: "dummy",
            name: "Dummy step",
            execute: () => ValueTask.FromResult(0));
        await ctx.WaitAll(dummy);

        return Unit.Value;
    }
}

using Spindle.Abstractions.Flows;

namespace Spindle.Example.LowLevel;

public record LowLevelFlowInput(string Str);
public record LowLevelFlowOutput(string Upper, string Lower, string CamelCase);

public class LowLevelFlow : ISpindleFlow<LowLevelFlowInput, LowLevelFlowOutput>
{
    public async ValueTask<LowLevelFlowOutput> RunAsync(IFlowContext ctx, LowLevelFlowInput request)
    {
        // Declare all steps
        var upper = ctx.Step<string>(
            id: "upper",
            name: "Generate Upper Representation",
            dependencies: [],
            execute: (_, _) => ValueTask.FromResult(request.Str.ToUpper()));
        var lower = ctx.Step<string>(
            id: "lower",
            name: "Generate Lower Representation",
            dependencies: [],
            execute: (_, _) => ValueTask.FromResult(request.Str.ToLower()));
        var camelCase = ctx.Step<string>(
            id: "camelCase",
            name: "Generate Camel-Cased Representation",
            dependencies: [],
            execute: (_, _) =>
            {
                var words = request.Str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var camelCased = string.Concat(words.Select((word, index) =>
                    index == 0 ? word.ToLower() : char.ToUpper(word[0]) + word.Substring(1).ToLower()));
                return ValueTask.FromResult(camelCased);
            });

        // Wait for all of them to complete in parallel
        await ctx.WaitAll(upper, lower, camelCase);

        // Build the result from the step results
        return new LowLevelFlowOutput(
            Upper: await upper,
            Lower: await lower,
            CamelCase: await camelCase
        );
    }
}
using Spindle.Abstractions.Core;
using Spindle.Abstractions.Flows;
using Spindle.Abstractions.Steps;

namespace Spindle.Example.Hosting;

public sealed record TextTransformRequest(string Text);

public sealed record TextTransformResult(
    string Upper,
    string Lower,
    string CamelCase,
    int Value);

public sealed class TextTransformFlow : ISpindleFlow<TextTransformRequest, TextTransformResult>
{
    public static FlowName Name { get; } = new("hosting-text-transform");

    public async ValueTask<TextTransformResult> RunAsync(
        IFlowContext ctx,
        TextTransformRequest request)
    {
        var upper = ctx.Step<string>(
            id: "upper",
            name: "Generate upper representation",
            execute: () => ValueTask.FromResult(request.Text.ToUpperInvariant()));

        var lower = ctx.Step<string>(
            id: "lower",
            name: "Generate lower representation",
            execute: () => ValueTask.FromResult(request.Text.ToLowerInvariant()));

        var camelCase = ctx.Step<string>(
            id: "camel-case",
            name: "Generate camel-cased representation",
            execute: () => ValueTask.FromResult(ToCamelCase(request.Text)));

        await ctx.WaitAll(upper, lower, camelCase);

        var lastStep = ctx.Step("d1", "Dummy Starter",
            () => ValueTask.FromResult(0));
        for (var i = 0; i < 100; i++)
        {
            var i1 = i;
            lastStep = ctx.Step(id: $"dummy-{i}",
                name: $"Dummy Step {i}",
                lastStep,
                execute: async val =>
                {
                    await Task.Delay(1);
                    return i1 + val;
                });
        }


        return new TextTransformResult(
            Upper: await upper,
            Lower: await lower,
            CamelCase: await camelCase,
            Value: await lastStep);
    }

    private static string ToCamelCase(
        string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return string.Concat(words.Select((word, index) =>
            index == 0
                ? word.ToLowerInvariant()
                : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }
}

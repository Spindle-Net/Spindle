using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

namespace Spindle.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance.AddExporter(JsonExporter.Full);
        var isFullRun = args.Contains("--full", StringComparer.OrdinalIgnoreCase);
        Environment.SetEnvironmentVariable(BenchmarkProfile.ProfileEnvironmentVariable, isFullRun ? "full" : null);

        var benchmarkDotNetArgs = args
            .Where(argument => !string.Equals(argument, "--full", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var effectiveArgs = benchmarkDotNetArgs.Length == 0 ? ["--filter", "*"] : benchmarkDotNetArgs;
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(effectiveArgs, config);
    }
}

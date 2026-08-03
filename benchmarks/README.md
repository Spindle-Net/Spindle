# Spindle.Net benchmarks

The benchmark suite runs complete local workflows through the real runtime and both persistence implementations:

- `InMemory`: end-to-end runtime overhead with the in-memory store.
- `Sqlite`: end-to-end runtime overhead through EF Core and a shared in-memory SQLite database.

The SQLite provider measures EF Core, SQL generation, transactions, SQLite locking, and serialization without disk noise. It does not include filesystem access, write-ahead logging, `fsync`, or physical disk latency, so it is not an estimate of file-backed production SQLite performance.

Each invocation starts from a fresh store; setup, SQLite migration, and existing-store seeding are excluded from timings. Each case uses two launches with twelve independent measured iterations per launch. Invocation count remains one because a completed workflow consumes its fresh store state. The suite contains four parameterized full-workflow matrices:

- Sequential chains: 8 through 1,024 steps, with 1–4 flows running concurrently.
- Concurrent tasks: 1–16 independent tasks per flow, with 1–64 flows running concurrently.
- Dependency graphs: fan-out, fan-in, and diamond graphs with widths 4–64 and 1–16 concurrent flows.
- Existing stores: one eight-step workflow against stores preloaded with 0, 100, or 1,000 completed flows.

Run the representative quick matrix:

```sh
dotnet run -c Release -f net10.0 --project benchmarks/Spindle.Benchmarks
```

Run every requested parameter combination:

```sh
dotnet run -c Release -f net10.0 --project benchmarks/Spindle.Benchmarks -- --full
```

Target one benchmark family or combine it with `--full`:

```sh
dotnet run -c Release -f net10.0 --project benchmarks/Spindle.Benchmarks -- --filter '*Sequential*'
dotnet run -c Release -f net10.0 --project benchmarks/Spindle.Benchmarks -- --full --filter '*Concurrent*'
```

Results are written to `BenchmarkDotNet.Artifacts/results/`. Save the generated full JSON before a change, run the same command after it, and compare runtime confidence intervals and allocations with:

```sh
scripts/benchmark-compare.sh before.json after.json
```

Generate a Markdown report from every full JSON export in the results directory:

```sh
scripts/benchmark-report.py
```

The report is written to `BenchmarkDotNet.Artifacts/benchmark-report.md` and includes the host/runtime metadata, family and provider coverage, runtime/allocation ranges, confidence intervals, and expandable per-case tables. Pass explicit JSON files and/or another output path when needed:

```sh
scripts/benchmark-report.py \
  BenchmarkDotNet.Artifacts/results/Spindle.Benchmarks.SequentialFlowBenchmarks-report-full.json \
  --output /tmp/spindle-benchmark-report.md
```

Benchmark timings are machine-specific. Compare runs from the same machine with no competing load, and use the SQLite results when evaluating persistence-path changes.

Runtime changes smaller than 5% are reported as unchanged. Larger changes are classified as improved or regressed only when the two confidence intervals do not overlap; otherwise they are inconclusive. Allocation changes must exceed both 5% and 1 KiB per operation. Performance classifications are advisory and do not make the comparison command fail, but malformed exports and benchmarks missing from the current run do.

#!/usr/bin/env python3
"""Create a Markdown summary from BenchmarkDotNet full JSON exports."""

from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import os
import sys
from collections import defaultdict
from pathlib import Path
from urllib.parse import parse_qsl


DEFAULT_INPUT_GLOB = "BenchmarkDotNet.Artifacts/results/*-report-full.json"
DEFAULT_OUTPUT = "BenchmarkDotNet.Artifacts/benchmark-report.md"


def finite_number(value: object, label: str) -> float:
    if not isinstance(value, (int, float)) or not math.isfinite(float(value)):
        raise ValueError(f"{label} must be a finite number")
    return float(value)


def format_duration_ns(value: float) -> str:
    if value < 1_000:
        return f"{value:.2f} ns"
    if value < 1_000_000:
        return f"{value / 1_000:.2f} us"
    if value < 1_000_000_000:
        return f"{value / 1_000_000:.2f} ms"
    return f"{value / 1_000_000_000:.3f} s"


def format_bytes(value: float | None) -> str:
    if value is None:
        return "n/a"
    if value < 1_024:
        return f"{value:.0f} B"
    if value < 1_048_576:
        return f"{value / 1_024:.2f} KiB"
    if value < 1_073_741_824:
        return f"{value / 1_048_576:.2f} MiB"
    return f"{value / 1_073_741_824:.2f} GiB"


def markdown_escape(value: object) -> str:
    return str(value).replace("\\", "\\\\").replace("|", "\\|").replace("\n", " ")


def parse_parameters(raw: object) -> dict[str, str]:
    if not isinstance(raw, str) or not raw:
        return {}
    return dict(parse_qsl(raw, keep_blank_values=True))


def parameter_label(parameters: dict[str, str], fallback: str) -> str:
    if not parameters:
        return fallback
    return ", ".join(f"{key}={value}" for key, value in parameters.items())


def family_name(path: Path) -> str:
    stem = path.name.removesuffix("-report-full.json")
    return stem.removeprefix("Spindle.Benchmarks.")


def load_report(path: Path) -> dict[str, object]:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"could not read {path}: {error}") from error

    benchmarks = payload.get("Benchmarks")
    if not isinstance(benchmarks, list) or not benchmarks:
        raise ValueError(f"{path} does not contain a non-empty Benchmarks array")

    records: list[dict[str, object]] = []
    for index, benchmark in enumerate(benchmarks, start=1):
        if not isinstance(benchmark, dict):
            raise ValueError(f"{path}: benchmark {index} is not an object")

        statistics = benchmark.get("Statistics")
        if not isinstance(statistics, dict):
            raise ValueError(f"{path}: benchmark {index} has no Statistics object")
        confidence = statistics.get("ConfidenceInterval")
        if not isinstance(confidence, dict):
            raise ValueError(f"{path}: benchmark {index} has no ConfidenceInterval object")

        memory = benchmark.get("Memory")
        allocated = None
        if isinstance(memory, dict) and memory.get("BytesAllocatedPerOperation") is not None:
            allocated = finite_number(
                memory["BytesAllocatedPerOperation"],
                f"{path}: benchmark {index} allocation",
            )

        full_name = benchmark.get("FullName") or benchmark.get("DisplayInfo")
        if not isinstance(full_name, str):
            raise ValueError(f"{path}: benchmark {index} has no FullName or DisplayInfo")

        parameters = parse_parameters(benchmark.get("Parameters"))
        records.append(
            {
                "full_name": full_name,
                "parameters": parameters,
                "parameter_label": parameter_label(parameters, full_name),
                "mean": finite_number(statistics.get("Mean"), f"{path}: benchmark {index} mean"),
                "lower": finite_number(
                    confidence.get("Lower"), f"{path}: benchmark {index} confidence lower bound"
                ),
                "upper": finite_number(
                    confidence.get("Upper"), f"{path}: benchmark {index} confidence upper bound"
                ),
                "allocated": allocated,
                "samples": statistics.get("N"),
            }
        )

    return {
        "path": path,
        "title": payload.get("Title") or family_name(path),
        "family": family_name(path),
        "environment": payload.get("HostEnvironmentInfo") or {},
        "records": records,
    }


def range_duration(records: list[dict[str, object]]) -> str:
    means = [float(record["mean"]) for record in records]
    return f"{format_duration_ns(min(means))} – {format_duration_ns(max(means))}"


def range_bytes(records: list[dict[str, object]]) -> str:
    values = [float(record["allocated"]) for record in records if record["allocated"] is not None]
    if not values:
        return "n/a"
    return f"{format_bytes(min(values))} – {format_bytes(max(values))}"


def relative_link(source: Path, output: Path) -> str:
    return Path(os.path.relpath(source, output.parent)).as_posix()


def build_markdown(reports: list[dict[str, object]], output: Path) -> str:
    all_records = [record for report in reports for record in report["records"]]
    environment = reports[0]["environment"]
    providers = sorted({record["parameters"].get("Provider", "n/a") for record in all_records})
    generated = dt.datetime.now(dt.UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")

    lines = [
        "# Spindle.Net benchmark report",
        "",
        f"Generated: `{generated}`",
        "",
        "This report is generated from BenchmarkDotNet full JSON exports. Means are measured per complete benchmark operation; allocations are managed bytes per operation.",
        "",
        "## Run summary",
        "",
        "| Field | Value |",
        "| --- | --- |",
        f"| BenchmarkDotNet | {markdown_escape(environment.get('BenchmarkDotNetVersion', 'n/a'))} |",
        f"| Runtime | {markdown_escape(environment.get('RuntimeVersion', 'n/a'))} |",
        f"| Processor | {markdown_escape(environment.get('ProcessorName', 'n/a'))} |",
        f"| Logical cores | {markdown_escape(environment.get('LogicalCoreCount', 'n/a'))} |",
        f"| Configuration | {markdown_escape(environment.get('Configuration', 'n/a'))} |",
        f"| Providers | {markdown_escape(', '.join(providers))} |",
        f"| Benchmark cases | {len(all_records)} |",
        "",
        "## Coverage",
        "",
        "| Family | Cases | Providers | Runtime range | Allocation range | Source |",
        "| --- | ---: | --- | --- | --- | --- |",
    ]

    for report in reports:
        records = report["records"]
        report_providers = sorted({record["parameters"].get("Provider", "n/a") for record in records})
        lines.append(
            f"| {markdown_escape(report['family'])} | {len(records)} | "
            f"{markdown_escape(', '.join(report_providers))} | {range_duration(records)} | "
            f"{range_bytes(records)} | [{markdown_escape(report['path'].name)}]({relative_link(report['path'], output)}) |"
        )

    lines.extend(
        [
            "",
            "## Provider summary",
            "",
            "| Family | Provider | Cases | Runtime range | Allocation range |",
            "| --- | --- | ---: | --- | --- |",
        ]
    )

    for report in reports:
        grouped: defaultdict[str, list[dict[str, object]]] = defaultdict(list)
        for record in report["records"]:
            grouped[record["parameters"].get("Provider", "n/a")].append(record)
        for provider, records in sorted(grouped.items()):
            lines.append(
                f"| {markdown_escape(report['family'])} | {markdown_escape(provider)} | {len(records)} | "
                f"{range_duration(records)} | {range_bytes(records)} |"
            )

    lines.extend(["", "## Detailed results", ""])
    for report in reports:
        records = report["records"]
        lines.extend(
            [
                f"### {markdown_escape(report['family'])}",
                "",
                f"Source: [{markdown_escape(report['path'].name)}]({relative_link(report['path'], output)})",
                "",
                "<details>",
                f"<summary>{len(records)} benchmark cases</summary>",
                "",
                "| Parameters | Mean | 99.9% CI | Allocated | Samples |",
                "| --- | ---: | --- | ---: | ---: |",
            ]
        )
        for record in sorted(records, key=lambda item: str(item["parameter_label"])):
            ci = f"{format_duration_ns(record['lower'])} – {format_duration_ns(record['upper'])}"
            samples = record["samples"] if isinstance(record["samples"], (int, float)) else "n/a"
            lines.append(
                f"| {markdown_escape(record['parameter_label'])} | {format_duration_ns(record['mean'])} | {ci} | "
                f"{format_bytes(record['allocated'])} | {samples} |"
            )
        lines.extend(["", "</details>", ""])

    lines.extend(
        [
            "## Interpretation",
            "",
            "- Compare runs from the same machine under similar system load.",
            "- Use the SQLite results for persistence-path changes. In-memory SQLite includes EF Core, SQL generation, transactions, locking, and serialization, but not filesystem, WAL, `fsync`, or physical-disk latency.",
            "- Use `scripts/benchmark-compare.sh baseline.json current.json` for confidence-aware before/after classifications.",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "inputs",
        nargs="*",
        type=Path,
        help=f"BenchmarkDotNet full JSON exports (default: {DEFAULT_INPUT_GLOB})",
    )
    parser.add_argument(
        "-o",
        "--output",
        type=Path,
        default=Path(DEFAULT_OUTPUT),
        help=f"Markdown output path (default: {DEFAULT_OUTPUT})",
    )
    args = parser.parse_args()

    paths = args.inputs or sorted(Path().glob(DEFAULT_INPUT_GLOB))
    if not paths:
        print(f"No BenchmarkDotNet full JSON exports found ({DEFAULT_INPUT_GLOB}).", file=sys.stderr)
        return 66

    try:
        reports = [load_report(path) for path in paths]
        output = args.output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(build_markdown(reports, output), encoding="utf-8")
    except (OSError, ValueError) as error:
        print(f"benchmark report failed: {error}", file=sys.stderr)
        return 65

    print(f"Wrote {output} ({sum(len(report['records']) for report in reports)} benchmark cases).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env sh
set -eu

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <baseline.json> <current.json>" >&2
  exit 64
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required to compare BenchmarkDotNet JSON exports." >&2
  exit 69
fi

baseline=$1
current=$2

for file in "$baseline" "$current"; do
  if [ ! -f "$file" ]; then
    echo "Benchmark result not found: $file" >&2
    exit 66
  fi
done

for file in "$baseline" "$current"; do
  if ! jq -e '
    (.Benchmarks | type == "array") and
    all(
      .Benchmarks[];
      ((.FullName // .DisplayInfo) | type == "string") and
      (.Statistics.Mean | type == "number") and
      (.Statistics.ConfidenceInterval.Lower | type == "number") and
      (.Statistics.ConfidenceInterval.Upper | type == "number")
    )
  ' "$file" >/dev/null; then
    echo "Invalid BenchmarkDotNet full JSON export: $file" >&2
    exit 65
  fi
done

printf 'Benchmark\tRuntime\tRuntime verdict\tAllocated\tAllocation verdict\n'

jq -r --slurpfile current_results "$current" '
  ($current_results[0].Benchmarks | map({key: (.FullName // .DisplayInfo), value: .}) | from_entries) as $current |
  .Benchmarks[] |
  (.FullName // .DisplayInfo) as $name |
  ($current[$name] // null) as $latest |
  if $latest == null then
    [$name, "missing", "", "", "", "", "", "", "", ""]
  else
    [
      $name,
      "present",
      .Statistics.Mean,
      .Statistics.ConfidenceInterval.Lower,
      .Statistics.ConfidenceInterval.Upper,
      (.Memory.BytesAllocatedPerOperation // "null"),
      $latest.Statistics.Mean,
      $latest.Statistics.ConfidenceInterval.Lower,
      $latest.Statistics.ConfidenceInterval.Upper,
      ($latest.Memory.BytesAllocatedPerOperation // "null")
    ]
  end |
  @tsv
' "$baseline" |
while IFS="$(printf '\t')" read -r name presence previous_mean previous_lower previous_upper previous_bytes latest_mean latest_lower latest_upper latest_bytes; do
  if [ "$presence" = "missing" ]; then
    printf '%s\tmissing from current run\tmissing\t-\tmissing\n' "$name"
    continue
  fi

  awk \
    -v name="$name" \
    -v previous_mean="$previous_mean" \
    -v previous_lower="$previous_lower" \
    -v previous_upper="$previous_upper" \
    -v previous_bytes="$previous_bytes" \
    -v latest_mean="$latest_mean" \
    -v latest_lower="$latest_lower" \
    -v latest_upper="$latest_upper" \
    -v latest_bytes="$latest_bytes" '
    BEGIN {
      runtime_percent = ((latest_mean - previous_mean) / previous_mean) * 100
      runtime_verdict = "unchanged"
      if (runtime_percent >= 5) {
        runtime_verdict = latest_lower > previous_upper ? "regressed" : "inconclusive"
      } else if (runtime_percent <= -5) {
        runtime_verdict = latest_upper < previous_lower ? "improved" : "inconclusive"
      }

      allocation_change = "unknown"
      allocation_verdict = "unknown"
      if (previous_bytes != "null" && latest_bytes != "null") {
        allocation_delta = latest_bytes - previous_bytes
        if (previous_bytes == 0) {
          allocation_percent = latest_bytes == 0 ? 0 : 100
        } else {
          allocation_percent = (allocation_delta / previous_bytes) * 100
        }

        allocation_verdict = "unchanged"
        if (allocation_delta >= 1024 && allocation_percent >= 5) {
          allocation_verdict = "regressed"
        } else if (allocation_delta <= -1024 && allocation_percent <= -5) {
          allocation_verdict = "improved"
        }

        allocation_change = sprintf("%.0f B -> %.0f B (%+.2f%%)", previous_bytes, latest_bytes, allocation_percent)
      }

      printf "%s\t%.2f ns -> %.2f ns (%+.2f%%)\t%s\t%s\t%s\n", name, previous_mean, latest_mean, runtime_percent, runtime_verdict, allocation_change, allocation_verdict
    }
  '
done

added=$(jq -r --slurpfile baseline_results "$baseline" '
  ($baseline_results[0].Benchmarks | map((.FullName // .DisplayInfo)) | unique) as $baseline_names |
  [.Benchmarks[] | (.FullName // .DisplayInfo) as $name | select($baseline_names | index($name) | not) | $name] | .[]
' "$current")

if [ -n "$added" ]; then
  printf '\nNew benchmarks in current run:\n'
  printf '%s\n' "$added"
fi

missing_count=$(jq -n --slurpfile baseline_results "$baseline" --slurpfile current_results "$current" '
  ($current_results[0].Benchmarks | map((.FullName // .DisplayInfo)) | unique) as $current_names |
  [$baseline_results[0].Benchmarks[] | (.FullName // .DisplayInfo) as $name | select($current_names | index($name) | not) | $name] | length
')

if [ "$missing_count" -ne 0 ]; then
  printf '\n%s benchmark(s) are missing from the current run.\n' "$missing_count" >&2
  exit 65
fi

#!/usr/bin/env sh
set -eu

# --- Color support ---------------------------------------------------------

if [ -t 1 ]; then
  RED=$(printf '\033[31m')
  GREEN=$(printf '\033[32m')
  YELLOW=$(printf '\033[33m')
  BLUE=$(printf '\033[34m')
  MAGENTA=$(printf '\033[35m')
  CYAN=$(printf '\033[36m')
  BOLD=$(printf '\033[1m')
  RESET=$(printf '\033[0m')
else
  RED=""
  GREEN=""
  YELLOW=""
  BLUE=""
  MAGENTA=""
  CYAN=""
  BOLD=""
  RESET=""
fi

error() {
  printf '%sError:%s %s\n' "${RED}${BOLD}" "${RESET}" "$*" >&2
}

# --- Arguments and prerequisites ------------------------------------------

if [ "$#" -ne 2 ]; then
  error "Usage: $0 <baseline.json> <current.json>"
  exit 64
fi

if ! command -v jq >/dev/null 2>&1; then
  error "jq is required to compare BenchmarkDotNet JSON exports."
  exit 69
fi

baseline=$1
current=$2

for file in "$baseline" "$current"; do
  if [ ! -f "$file" ]; then
    error "Benchmark result not found: $file"
    exit 66
  fi
done

# --- Validate JSON structure ----------------------------------------------

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
    error "Invalid BenchmarkDotNet full JSON export: $file"
    exit 65
  fi
done

# --- Temporary workspace ---------------------------------------------------

tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT INT TERM HUP

metrics_tsv="$tmpdir/metrics.tsv"

# --- Compute metrics with jq ----------------------------------------------

jq -r --slurpfile current_results "$current" '
  # Index current benchmarks by name
  ($current_results[0].Benchmarks
   | map({ key: (.FullName // .DisplayInfo), value: . })
   | from_entries) as $current
  |
  .Benchmarks[]
  | (.FullName // .DisplayInfo) as $name
  | ($current[$name] // null) as $latest
  | if $latest == null then
      # Benchmark missing from current run
      [
        $name,
        "missing",
        "null",    # previous_mean
        "null",    # latest_mean
        "null",    # runtime_percent
        "missing", # runtime_verdict
        "null",    # previous_bytes
        "null",    # latest_bytes
        "null",    # allocation_percent
        "missing"  # allocation_verdict
      ]
    else
      .Statistics as $prev
      | $latest.Statistics as $cur

      | $prev.Mean as $previous_mean
      | $cur.Mean  as $latest_mean

      | (($latest_mean - $previous_mean) / $previous_mean * 100)
        as $runtime_percent

      | $prev.ConfidenceInterval.Lower as $previous_lower
      | $prev.ConfidenceInterval.Upper as $previous_upper
      | $cur.ConfidenceInterval.Lower  as $latest_lower
      | $cur.ConfidenceInterval.Upper  as $latest_upper

      | (
          if   $runtime_percent >= 5 then
            if $latest_lower > $previous_upper then "regressed"
            else "inconclusive" end
          elif $runtime_percent <= -5 then
            if $latest_upper < $previous_lower then "improved"
            else "inconclusive" end
          else
            "unchanged"
          end
        ) as $runtime_verdict

      | (.Memory.BytesAllocatedPerOperation // null) as $previous_bytes
      | ($latest.Memory.BytesAllocatedPerOperation // null) as $latest_bytes

      | (
          if ($previous_bytes == null or $latest_bytes == null) then
            [ "null", "unknown" ]
          else
            ($latest_bytes - $previous_bytes) as $allocation_delta
            | (
                if   $previous_bytes == 0 then
                  (if $latest_bytes == 0 then 0 else 100 end)
                else
                  ($allocation_delta / $previous_bytes * 100)
                end
              ) as $allocation_percent
            | (
                if   $allocation_delta >= 1024 and $allocation_percent >= 5 then
                  "regressed"
                elif $allocation_delta <= -1024 and $allocation_percent <= -5 then
                  "improved"
                else
                  "unchanged"
                end
              ) as $allocation_verdict
            | [ $allocation_percent, $allocation_verdict ]
          end
        ) as $allocation_info

      | [
          $name,
          "present",
          $previous_mean,
          $latest_mean,
          $runtime_percent,
          $runtime_verdict,
          ($previous_bytes // "null"),
          ($latest_bytes // "null"),
          $allocation_info[0],
          $allocation_info[1]
        ]
    end
  | @tsv
' "$baseline" > "$metrics_tsv"

# --- Per-benchmark output and summary collection --------------------------

summary_runtime_improved="$tmpdir/runtime_improved"
summary_runtime_regressed="$tmpdir/runtime_regressed"
summary_allocation_improved="$tmpdir/allocation_improved"
summary_allocation_regressed="$tmpdir/allocation_regressed"

: > "$summary_runtime_improved"
: > "$summary_runtime_regressed"
: > "$summary_allocation_improved"
: > "$summary_allocation_regressed"

# Header
printf '%bBenchmark%b\t%bRuntime%b\t%bRuntime verdict%b\t%bAllocated%b\t%bAllocation verdict%b\n' \
  "${BOLD}${CYAN}" "${RESET}" \
  "${BOLD}${CYAN}" "${RESET}" \
  "${BOLD}${CYAN}" "${RESET}" \
  "${BOLD}${CYAN}" "${RESET}" \
  "${BOLD}${CYAN}" "${RESET}"

while IFS=$(printf '\t') read -r \
  name presence previous_mean latest_mean \
  runtime_percent runtime_verdict \
  previous_bytes latest_bytes \
  allocation_percent allocation_verdict
do
  # Skip possible empty line at end
  [ -z "${name:-}" ] && continue

  if [ "$presence" = "missing" ]; then
    runtime_str="${YELLOW}missing from current run${RESET}"
    runtime_verdict_str="${YELLOW}${BOLD}missing${RESET}"
    allocation_str="-"
    allocation_verdict_str="${YELLOW}${BOLD}missing${RESET}"

    printf '%s\t%s\t%s\t%s\t%s\n' \
      "$name" "$runtime_str" "$runtime_verdict_str" "$allocation_str" "$allocation_verdict_str"
    continue
  fi

  # --- Runtime formatting --------------------------------------------------

  runtime_color=$RESET
  case "$runtime_verdict" in
    improved)     runtime_color=$GREEN ;;
    regressed)    runtime_color=$RED ;;
    inconclusive) runtime_color=$YELLOW ;;
    unchanged)    runtime_color=$RESET ;;
  esac

  runtime_plain=$(printf '%.2f ns -> %.2f ns (%+.2f%%)' \
    "$previous_mean" "$latest_mean" "$runtime_percent")
  runtime_str="${runtime_color}${runtime_plain}${RESET}"
  runtime_verdict_str="${runtime_color}${BOLD}${runtime_verdict}${RESET}"

  # --- Allocation formatting ----------------------------------------------

  allocation_plain="unknown"
  allocation_color=$YELLOW

  if [ "$allocation_verdict" = "unknown" ] \
     || [ "$previous_bytes" = "null" ] \
     || [ "$latest_bytes" = "null" ] \
     || [ "$allocation_percent" = "null" ]; then

    allocation_str="${allocation_color}${allocation_plain}${RESET}"
    allocation_verdict_str="${allocation_color}${BOLD}${allocation_verdict}${RESET}"
  else
    case "$allocation_verdict" in
      improved)  allocation_color=$GREEN ;;
      regressed) allocation_color=$RED ;;
      unchanged) allocation_color=$RESET ;;
    esac

    allocation_plain=$(printf '%d B -> %d B (%+.2f%%)' \
      "$previous_bytes" "$latest_bytes" "$allocation_percent")
    allocation_str="${allocation_color}${allocation_plain}${RESET}"
    allocation_verdict_str="${allocation_color}${BOLD}${allocation_verdict}${RESET}"
  fi

  # --- Print row -----------------------------------------------------------

  printf '%s\t%s\t%s\t%s\t%s\n' \
    "$name" "$runtime_str" "$runtime_verdict_str" "$allocation_str" "$allocation_verdict_str"

  # --- Collect summary info -----------------------------------------------

  case "$runtime_verdict" in
    improved)
      printf '%s: %s\n' "$name" "$runtime_plain" >> "$summary_runtime_improved"
      ;;
    regressed)
      printf '%s: %s\n' "$name" "$runtime_plain" >> "$summary_runtime_regressed"
      ;;
  esac

  case "$allocation_verdict" in
    improved)
      printf '%s: %s\n' "$name" "$allocation_plain" >> "$summary_allocation_improved"
      ;;
    regressed)
      printf '%s: %s\n' "$name" "$allocation_plain" >> "$summary_allocation_regressed"
      ;;
  esac

done < "$metrics_tsv"

# --- Summary --------------------------------------------------------------

printf '\n%bSummary%b\n' "$BOLD" "$RESET"

if [ -s "$summary_runtime_improved" ]; then
  printf '\n%sRuntime improvements:%s\n' "${GREEN}${BOLD}" "$RESET"
  sed 's/^/  - /' "$summary_runtime_improved"
else
  printf '\n%sRuntime improvements:%s none\n' "$BOLD" "$RESET"
fi

if [ -s "$summary_runtime_regressed" ]; then
  printf '\n%sRuntime regressions:%s\n' "${RED}${BOLD}" "$RESET"
  sed 's/^/  - /' "$summary_runtime_regressed"
else
  printf '\n%sRuntime regressions:%s none\n' "$BOLD" "$RESET"
fi

if [ -s "$summary_allocation_improved" ]; then
  printf '\n%sAllocation improvements:%s\n' "${GREEN}${BOLD}" "$RESET"
  sed 's/^/  - /' "$summary_allocation_improved"
else
  printf '\n%sAllocation improvements:%s none\n' "$BOLD" "$RESET"
fi

if [ -s "$summary_allocation_regressed" ]; then
  printf '\n%sAllocation regressions:%s\n' "${RED}${BOLD}" "$RESET"
  sed 's/^/  - /' "$summary_allocation_regressed"
else
  printf '\n%sAllocation regressions:%s none\n' "$BOLD" "$RESET"
fi

# --- New and missing benchmarks -------------------------------------------

added=$(jq -r --slurpfile baseline_results "$baseline" '
  ($baseline_results[0].Benchmarks
   | map((.FullName // .DisplayInfo))
   | unique) as $baseline_names
  |
  [
    .Benchmarks[]
    | (.FullName // .DisplayInfo) as $name
    | select($baseline_names | index($name) | not)
    | $name
  ]
  | .[]
' "$current")

if [ -n "$added" ]; then
  printf '\n%sNew benchmarks in current run:%s\n' "${CYAN}${BOLD}" "$RESET"
  printf '%s\n' "$added" | sed 's/^/  - /'
fi

missing_count=$(jq -n \
  --slurpfile baseline_results "$baseline" \
  --slurpfile current_results "$current" '
  ($current_results[0].Benchmarks
   | map((.FullName // .DisplayInfo))
   | unique) as $current_names
  |
  [
    $baseline_results[0].Benchmarks[]
    | (.FullName // .DisplayInfo) as $name
    | select($current_names | index($name) | not)
    | $name
  ]
  | length
')

if [ "$missing_count" -ne 0 ]; then
  printf '\n%s%s benchmark(s) are missing from the current run.%s\n' \
    "${RED}${BOLD}" "$missing_count" "$RESET" >&2
  exit 65
fi
#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
subject="$repository_root/tools/performance/measure-http-workflow.sh"
temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT

help_output="$temporary_directory/help-output"
bash "$subject" --help >"$help_output"
if ! grep -q -- '--output-samples PATH.*one numeric ms value per line' "$help_output"; then
  printf '%s\n' 'FAIL: help does not document --output-samples PATH.' >&2
  exit 1
fi
if ! grep -q -- '--concurrency COUNT.*default: 1' "$help_output"; then
  printf '%s\n' 'FAIL: help does not document --concurrency COUNT.' >&2
  exit 1
fi

missing_value_output="$temporary_directory/missing-value-output"
set +e
bash "$subject" --output-samples >"$missing_value_output" 2>&1
missing_value_status=$?
set -e
if [[ "$missing_value_status" -ne 2 ]] || ! grep -q -- '--output-samples requires a value' "$missing_value_output"; then
  printf 'FAIL: missing --output-samples value returned %s without the expected usage error.\n' "$missing_value_status" >&2
  exit 1
fi

fake_bin="$temporary_directory/bin"
mkdir -p "$fake_bin"
cat >"$fake_bin/curl" <<'SH'
#!/usr/bin/env bash
set -euo pipefail

output_file=""
while (($# > 0)); do
  case "$1" in
    --output) output_file="$2"; shift 2 ;;
    --write-out) shift 2 ;;
    --silent|--show-error|--insecure) shift ;;
    *) shift ;;
  esac
done

count=0
while ! mkdir "$FAKE_CURL_COUNTER.lock" 2>/dev/null; do
  sleep 0.01
done
trap 'rmdir "$FAKE_CURL_COUNTER.lock"' EXIT
if [[ -f "$FAKE_CURL_COUNTER" ]]; then
  count="$(<"$FAKE_CURL_COUNTER")"
fi
count=$((count + 1))
printf '%s' "$count" >"$FAKE_CURL_COUNTER"
case "$count" in
  1) seconds=0.901 ;;
  2) seconds=0.801 ;;
  3) seconds=0.802 ;;
  4) seconds=0.010 ;;
  5) seconds=0.020 ;;
  6) seconds=0.030 ;;
  *) seconds=0.040 ;;
esac
printf 'Hello World!' >"$output_file"
printf '200 %s' "$seconds"
SH
chmod +x "$fake_bin/curl"

export PATH="$fake_bin:$PATH"
export FAKE_CURL_COUNTER="$temporary_directory/curl-counter"
samples_path="$temporary_directory/nested/results/warm-samples.txt"
json_output="$temporary_directory/report.json"
bash "$subject" \
  --url http://127.0.0.1:7243/workflows/http/hello-world \
  --expected-body 'Hello World!' \
  --warmup 2 \
  --requests 3 \
  --output-samples "$samples_path" >"$json_output"

if [[ ! -f "$samples_path" ]]; then
  printf '%s\n' 'FAIL: --output-samples did not create its parent directory and output file.' >&2
  exit 1
fi
if ! cmp -s <(printf '10.000\n20.000\n30.000\n') "$samples_path"; then
  printf '%s\n' 'FAIL: sample output did not contain exactly the measured warm latencies.' >&2
  sed 's/^/  | /' "$samples_path" >&2
  exit 1
fi
if [[ "$(wc -l <"$samples_path" | tr -d ' ')" -ne 3 ]]; then
  printf '%s\n' 'FAIL: sample output count did not match --requests.' >&2
  exit 1
fi
if ! jq -e '.samples == {warmup: 2, measured: 3} and .latencyMs == {cold: 901, min: 10, mean: 20, p50: 20, p95: 30, p99: 30, max: 30}' "$json_output" >/dev/null; then
  printf '%s\n' 'FAIL: report aggregates were not computed from the persisted warm samples.' >&2
  exit 1
fi

rm -f "$FAKE_CURL_COUNTER"
parallel_samples_path="$temporary_directory/parallel-samples.txt"
bash "$subject" \
  --url http://127.0.0.1:7243/workflows/http/hello-world \
  --expected-body 'Hello World!' \
  --warmup 0 \
  --requests 4 \
  --concurrency 2 \
  --output-samples "$parallel_samples_path" >"$temporary_directory/parallel-report.json"
if ! jq -e '.configuration.concurrency == 2 and .samples == {warmup: 0, measured: 4}' \
  "$temporary_directory/parallel-report.json" >/dev/null; then
  printf '%s\n' 'FAIL: concurrent invocation did not record its concurrency and sample count.' >&2
  exit 1
fi
if [[ "$(wc -l <"$parallel_samples_path" | tr -d ' ')" -ne 4 ]]; then
  printf '%s\n' 'FAIL: concurrent sample output count did not match --requests.' >&2
  exit 1
fi

rm -f "$FAKE_CURL_COUNTER"
bash "$subject" \
  --url http://127.0.0.1:7243/workflows/http/hello-world \
  --expected-body 'Hello World!' \
  --warmup 0 \
  --requests 1 >"$temporary_directory/default-report.json"
if ! jq -e '.samples == {warmup: 0, measured: 1} and .latencyMs == {cold: 901, min: 801, mean: 801, p50: 801, p95: 801, p99: 801, max: 801}' \
  "$temporary_directory/default-report.json" >/dev/null; then
  printf '%s\n' 'FAIL: invocation without --output-samples changed the default JSON output behavior.' >&2
  exit 1
fi

# --passes: enforcement takes the median of the per-pass p95 values, not the pooled p95, so a single
# pass disturbed by shared-runner scheduling cannot fail the gate on its own.
rm -f "$FAKE_CURL_COUNTER"
passes_report="$temporary_directory/passes-report.json"
bash "$subject" \
  --url http://127.0.0.1:7243/workflows/http/hello-world \
  --expected-body 'Hello World!' \
  --warmup 0 \
  --requests 1 \
  --passes 3 \
  --enforce-p95-ms 801 >"$passes_report"
if ! jq -e '.enforcement == {passes: 3, p95PerPass: [10, 801, 802], medianP95Ms: 801, budgetMs: 801}' \
  "$passes_report" >/dev/null; then
  printf '%s\n' 'FAIL: --passes did not report per-pass p95 values and their median.' >&2
  jq -c '.enforcement' "$passes_report" >&2
  exit 1
fi
# The pooled p95 across all three passes is 802, so a budget of 801 would have failed under the old
# single-sample rule. Passing here is the whole point of the change.
if ! jq -e '.latencyMs.p95 == 802' "$passes_report" >/dev/null; then
  printf '%s\n' 'FAIL: pooled latencyMs.p95 no longer describes every measured sample.' >&2
  exit 1
fi

rm -f "$FAKE_CURL_COUNTER"
set +e
bash "$subject" \
  --url http://127.0.0.1:7243/workflows/http/hello-world \
  --expected-body 'Hello World!' \
  --warmup 0 \
  --requests 1 \
  --passes 3 \
  --enforce-p95-ms 800 >"$temporary_directory/passes-fail.json" 2>"$temporary_directory/passes-fail.err"
passes_fail_status=$?
set -e
if [[ "$passes_fail_status" -eq 0 ]] || ! grep -q 'median of 3 pass' "$temporary_directory/passes-fail.err"; then
  printf 'FAIL: median p95 above budget did not fail the gate (status %s).\n' "$passes_fail_status" >&2
  cat "$temporary_directory/passes-fail.err" >&2
  exit 1
fi

rm -f "$FAKE_CURL_COUNTER"
set +e
bash "$subject" --url http://127.0.0.1:7243/workflows/http/hello-world \
  --expected-body 'Hello World!' --passes 0 >"$temporary_directory/passes-invalid" 2>&1
passes_invalid_status=$?
set -e
if [[ "$passes_invalid_status" -ne 2 ]] || ! grep -q -- '--passes requires a positive integer' "$temporary_directory/passes-invalid"; then
  printf 'FAIL: --passes 0 returned %s without the expected usage error.\n' "$passes_invalid_status" >&2
  exit 1
fi

printf '%s\n' 'PASS: measure-http-workflow sample output contract.'

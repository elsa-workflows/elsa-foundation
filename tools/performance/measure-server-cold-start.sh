#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage: measure-server-cold-start.sh [options]

Required:
  --server-dll PATH             Prebuilt Elsa.Server DLL.
  --content-root PATH           Frozen server configuration/content directory.
  --baseline-dir PATH           Frozen data files copied into every boot.
  --base-url URL                Loopback server URL, for example http://127.0.0.1:7243.
  --readiness-path PATH         Readiness endpoint (normally /health/ready).
  --workflow-path PATH          Workflow endpoint to validate.
  --expected-status CODE        Expected workflow HTTP status.
  --expected-body TEXT          Exact expected workflow response body.
  --boots COUNT                 Positive number of isolated boots.

Reports and limits:
  --output-json PATH            JSON report path (default: artifacts directory/report.json).
  --output-markdown PATH        Markdown report path (default: artifacts directory/report.md).
  --artifacts-dir PATH          Retained per-boot files and logs.
  --liveness-path PATH          Listening probe path (default: /health/live).
  --startup-timeout-seconds N   Per-milestone timeout (default: 120).
  --enforce-ready-p95-ms N      Fail when shell-ready p95 exceeds this budget.
  --enforce-first-request-p95-ms N
                                Fail when first-workflow-request p95 exceeds this budget.
  --help                        Show this help.
EOF
}

die_usage() {
  printf 'ERROR: %s\n' "$1" >&2
  exit 2
}

require_value() {
  [[ $# -ge 2 && -n "$2" ]] || die_usage "$1 requires a value"
}

server_dll=""
content_root=""
baseline_dir=""
base_url=""
readiness_path=""
workflow_path=""
expected_status=""
expected_body=""
boots=""
output_json=""
output_markdown=""
artifacts_dir=""
liveness_path="/health/live"
startup_timeout_seconds=120
ready_budget_ms=""
workflow_budget_ms=""

while (($# > 0)); do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --server-dll) require_value "$@"; server_dll="$2"; shift 2 ;;
    --content-root) require_value "$@"; content_root="$2"; shift 2 ;;
    --baseline-dir) require_value "$@"; baseline_dir="$2"; shift 2 ;;
    --base-url) require_value "$@"; base_url="${2%/}"; shift 2 ;;
    --readiness-path) require_value "$@"; readiness_path="$2"; shift 2 ;;
    --workflow-path) require_value "$@"; workflow_path="$2"; shift 2 ;;
    --expected-status) require_value "$@"; expected_status="$2"; shift 2 ;;
    --expected-body) require_value "$@"; expected_body="$2"; shift 2 ;;
    --boots) require_value "$@"; boots="$2"; shift 2 ;;
    --output-json) require_value "$@"; output_json="$2"; shift 2 ;;
    --output-markdown) require_value "$@"; output_markdown="$2"; shift 2 ;;
    --artifacts-dir) require_value "$@"; artifacts_dir="$2"; shift 2 ;;
    --liveness-path) require_value "$@"; liveness_path="$2"; shift 2 ;;
    --startup-timeout-seconds) require_value "$@"; startup_timeout_seconds="$2"; shift 2 ;;
    --enforce-ready-p95-ms) require_value "$@"; ready_budget_ms="$2"; shift 2 ;;
    --enforce-first-request-p95-ms|--enforce-workflow-p95-ms)
      require_value "$@"; workflow_budget_ms="$2"; shift 2 ;;
    *) die_usage "Unknown argument: $1" ;;
  esac
done

[[ -n "$server_dll" ]] || die_usage "--server-dll is required"
[[ -n "$content_root" ]] || die_usage "--content-root is required"
[[ -n "$baseline_dir" ]] || die_usage "--baseline-dir is required"
[[ -n "$base_url" ]] || die_usage "--base-url is required"
[[ -n "$readiness_path" ]] || die_usage "--readiness-path is required"
[[ -n "$workflow_path" ]] || die_usage "--workflow-path is required"
[[ -n "$expected_status" ]] || die_usage "--expected-status is required"
[[ -n "$expected_body" ]] || die_usage "--expected-body is required"
[[ "$boots" =~ ^[1-9][0-9]*$ ]] || die_usage "--boots must be a positive integer"
[[ "$startup_timeout_seconds" =~ ^[1-9][0-9]*$ ]] || die_usage "--startup-timeout-seconds must be a positive integer"
[[ "$expected_status" =~ ^[1-5][0-9][0-9]$ ]] || die_usage "--expected-status must be a three-digit HTTP status"
for budget in "$ready_budget_ms" "$workflow_budget_ms"; do
  [[ -z "$budget" || "$budget" =~ ^[0-9]+([.][0-9]+)?$ ]] || die_usage "performance budgets must be non-negative numbers"
done

[[ -f "$server_dll" ]] || die_usage "Server DLL not found: $server_dll"
[[ -d "$content_root" ]] || die_usage "Content root not found: $content_root"
[[ -d "$baseline_dir" ]] || die_usage "Baseline directory not found: $baseline_dir"
for tool in dotnet curl python3; do
  command -v "$tool" >/dev/null 2>&1 || die_usage "Required tool not found: $tool"
done

read -r url_host url_port < <(python3 - "$base_url" <<'PY'
import sys
from urllib.parse import urlparse

value = urlparse(sys.argv[1])
if value.scheme not in ("http", "https") or value.hostname not in ("127.0.0.1", "localhost", "::1") or not value.port:
    raise SystemExit("--base-url must be an explicit loopback URL with a port")
print(value.hostname, value.port)
PY
) || die_usage "--base-url must be an explicit loopback URL with a port"

if python3 - "$url_host" "$url_port" <<'PY'
import socket, sys
try:
    with socket.create_connection((sys.argv[1], int(sys.argv[2])), timeout=.25):
        pass
except OSError:
    raise SystemExit(1)
PY
then
  die_usage "Loopback port is occupied or unavailable: $url_host:$url_port"
fi

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
if [[ -z "$artifacts_dir" ]]; then
  artifacts_dir="${TMPDIR:-/tmp}/elsa-cold-start-$timestamp"
fi
mkdir -p "$artifacts_dir"
output_json="${output_json:-$artifacts_dir/report.json}"
output_markdown="${output_markdown:-$artifacts_dir/report.md}"
mkdir -p "$(dirname "$output_json")" "$(dirname "$output_markdown")"
results_tsv="$artifacts_dir/boots.tsv"
printf 'boot\tlistening_ms\tactivation_ms\tshell_ready_ms\tfirst_request_ms\tfirst_success_ms\tshutdown_ms\n' >"$results_tsv"

server_pid=""
cleanup_process() {
  if [[ -n "$server_pid" ]] && kill -0 "$server_pid" 2>/dev/null; then
    kill "$server_pid" 2>/dev/null || true
    for _ in {1..50}; do
      kill -0 "$server_pid" 2>/dev/null || break
      sleep 0.1
    done
    kill -9 "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
  server_pid=""
}
trap cleanup_process EXIT INT TERM

now_ns() {
  python3 - <<'PY'
import time
print(time.monotonic_ns())
PY
}

elapsed_ms() {
  python3 - "$1" "$2" <<'PY'
import sys
print(f"{(int(sys.argv[2]) - int(sys.argv[1])) / 1_000_000:.3f}")
PY
}

wait_for_status() {
  local url="$1" expected="$2" deadline_ns="$3" body_file="$4"
  local status
  while :; do
    status="$(curl --silent --show-error --max-time 1 --output "$body_file" --write-out '%{http_code}' "$url" 2>/dev/null || true)"
    [[ "$status" == "$expected" ]] && return 0
    if ! kill -0 "$server_pid" 2>/dev/null; then
      return 1
    fi
    (( $(now_ns) < deadline_ns )) || return 1
    sleep 0.05
  done
}

content_root="$(cd "$content_root" && pwd)"
baseline_dir="$(cd "$baseline_dir" && pwd)"
server_dll="$(cd "$(dirname "$server_dll")" && pwd)/$(basename "$server_dll")"
dotnet_version="$(dotnet --version)"
git_commit="$(git -C "$(dirname "${BASH_SOURCE[0]}")/../.." rev-parse HEAD 2>/dev/null || printf unknown)"
baseline_hash="$(python3 - "$baseline_dir" <<'PY'
import hashlib, pathlib, sys
root = pathlib.Path(sys.argv[1])
h = hashlib.sha256()
for path in sorted(p for p in root.rglob('*') if p.is_file()):
    h.update(str(path.relative_to(root)).encode())
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
print(h.hexdigest())
PY
)"

for ((boot=1; boot<=boots; boot++)); do
  run_dir="$artifacts_dir/boot-$(printf '%03d' "$boot")"
  work_dir="$run_dir/content"
  mkdir -p "$work_dir"
  cp -R "$content_root"/. "$work_dir"/
  cp -R "$baseline_dir"/. "$work_dir"/
  log_file="$run_dir/server.log"
  live_body="$run_dir/live.body"
  ready_body="$run_dir/ready.body"
  workflow_body="$run_dir/workflow.body"

  started_ns="$(now_ns)"
  (
    cd "$work_dir"
    ASPNETCORE_URLS="$base_url" DOTNET_ENVIRONMENT=Production \
      dotnet "$server_dll"
  ) >"$log_file" 2>&1 &
  server_pid=$!
  deadline_ns=$((started_ns + startup_timeout_seconds * 1000000000))

  if ! wait_for_status "$base_url$liveness_path" 200 "$deadline_ns" "$live_body"; then
    printf 'ERROR: boot %s did not begin listening; retained log: %s\n' "$boot" "$log_file" >&2
    exit 1
  fi
  listening_ns="$(now_ns)"
  if ! wait_for_status "$base_url$readiness_path" 200 "$deadline_ns" "$ready_body"; then
    printf 'ERROR: boot %s did not become ready; retained log: %s\n' "$boot" "$log_file" >&2
    exit 1
  fi
  ready_ns="$(now_ns)"

  workflow_status="$(curl --silent --show-error --max-time "$startup_timeout_seconds" \
    --output "$workflow_body" --write-out '%{http_code}' "$base_url$workflow_path" || true)"
  workflow_ns="$(now_ns)"
  actual_body="$(cat "$workflow_body" 2>/dev/null || true)"
  if [[ "$workflow_status" != "$expected_status" || "$actual_body" != "$expected_body" ]]; then
    printf 'ERROR: boot %s workflow validation failed (status %s, expected %s); retained files: %s\n' \
      "$boot" "$workflow_status" "$expected_status" "$run_dir" >&2
    exit 1
  fi

  shutdown_started_ns="$(now_ns)"
  kill "$server_pid" 2>/dev/null || true
  for _ in {1..100}; do
    kill -0 "$server_pid" 2>/dev/null || break
    sleep 0.05
  done
  if kill -0 "$server_pid" 2>/dev/null; then
    printf 'ERROR: boot %s did not shut down cleanly; retained log: %s\n' "$boot" "$log_file" >&2
    exit 1
  fi
  wait "$server_pid" 2>/dev/null || true
  server_pid=""
  shutdown_ns="$(now_ns)"

  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$boot" "$(elapsed_ms "$started_ns" "$listening_ns")" \
    "$(elapsed_ms "$listening_ns" "$ready_ns")" "$(elapsed_ms "$started_ns" "$ready_ns")" \
    "$(elapsed_ms "$ready_ns" "$workflow_ns")" "$(elapsed_ms "$started_ns" "$workflow_ns")" \
    "$(elapsed_ms "$shutdown_started_ns" "$shutdown_ns")" >>"$results_tsv"
done

python3 - "$results_tsv" "$output_json" "$output_markdown" "$git_commit" "$dotnet_version" \
  "$baseline_hash" "$base_url" "$readiness_path" "$workflow_path" "$ready_budget_ms" "$workflow_budget_ms" <<'PY'
import csv, datetime, json, math, pathlib, statistics, sys

tsv, json_path, markdown_path, commit, dotnet, baseline_hash, base_url, readiness_path, workflow_path, ready_budget, workflow_budget = sys.argv[1:]
with open(tsv, newline='') as stream:
    rows = [{k: (int(v) if k == 'boot' else float(v)) for k, v in row.items()} for row in csv.DictReader(stream, delimiter='\t')]

def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]

def aggregate(key):
    values = [row[key] for row in rows]
    return {"p50Ms": percentile(values, .50), "p95Ms": percentile(values, .95), "minMs": min(values), "maxMs": max(values)}

milestones = ("listening_ms", "activation_ms", "shell_ready_ms", "first_request_ms", "first_success_ms")
aggregates = {key: aggregate(key) for key in (*milestones, "shutdown_ms")}
report = {
    "generatedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "provenance": {"gitCommit": commit, "dotnetVersion": dotnet, "baselineSha256": baseline_hash},
    "request": {"baseUrl": base_url, "readinessPath": readiness_path, "workflowPath": workflow_path, "boots": len(rows)},
    "boots": rows,
    "aggregates": aggregates,
}
pathlib.Path(json_path).write_text(json.dumps(report, indent=2) + "\n")
lines = [
    "# Elsa server cold-start report", "",
    f"- Boots: {len(rows)}", f"- Git commit: `{commit}`", f"- .NET SDK: `{dotnet}`", f"- Baseline SHA-256: `{baseline_hash}`", "",
    "| Milestone | p50 (ms) | p95 (ms) | min (ms) | max (ms) |", "|---|---:|---:|---:|---:|",
]
for key, label in (("listening_ms", "Listening"), ("activation_ms", "Activation"), ("shell_ready_ms", "Shell ready"), ("first_request_ms", "First workflow request"), ("first_success_ms", "First success"), ("shutdown_ms", "Shutdown")):
    item = aggregates[key]
    lines.append(f"| {label} | {item['p50Ms']:.3f} | {item['p95Ms']:.3f} | {item['minMs']:.3f} | {item['maxMs']:.3f} |")
pathlib.Path(markdown_path).write_text("\n".join(lines) + "\n")

failed = []
if ready_budget and aggregates["shell_ready_ms"]["p95Ms"] > float(ready_budget):
    failed.append(f"ready p95 {aggregates['shell_ready_ms']['p95Ms']:.3f} ms exceeds budget {float(ready_budget):.3f} ms")
if workflow_budget and aggregates["first_request_ms"]["p95Ms"] > float(workflow_budget):
    failed.append(f"workflow p95 {aggregates['first_request_ms']['p95Ms']:.3f} ms exceeds budget {float(workflow_budget):.3f} ms")
if failed:
    print("Budget failed: " + "; ".join(failed), file=sys.stderr)
    raise SystemExit(1)
PY

printf 'Cold-start report: %s\nMarkdown summary: %s\nRetained boot artifacts: %s\n' \
  "$output_json" "$output_markdown" "$artifacts_dir"

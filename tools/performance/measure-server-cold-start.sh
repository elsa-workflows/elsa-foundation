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
  --expected-shell NAME         Shell expected in readiness JSON (default: default).
  --workflow-path PATH          Workflow endpoint to validate.
  --expected-status CODE        Expected workflow HTTP status.
  --expected-body TEXT          Exact expected workflow response body.
  --boots COUNT                 Positive number of isolated boots.

Reports and limits:
  --output-json PATH            JSON report path (default: artifacts directory/report.json).
  --output-markdown PATH        Markdown report path (default: artifacts directory/report.md).
  --artifacts-dir PATH          Reports plus failed-boot files and logs.
  --liveness-path PATH          Listening probe path (default: /health/live).
  --startup-timeout-seconds N   Per-milestone timeout (default: 120).
  --shutdown-timeout-seconds N  Graceful process-group shutdown timeout (default: 30).
  --retain-success-artifacts    Keep successful boot logs and mutable data copies.
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
expected_shell="default"
workflow_path=""
expected_status=""
expected_body=""
boots=""
output_json=""
output_markdown=""
artifacts_dir=""
liveness_path="/health/live"
startup_timeout_seconds=120
shutdown_timeout_seconds=30
ready_budget_ms=""
workflow_budget_ms=""
retain_success_artifacts=false

while (($# > 0)); do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --server-dll) require_value "$@"; server_dll="$2"; shift 2 ;;
    --content-root) require_value "$@"; content_root="$2"; shift 2 ;;
    --baseline-dir) require_value "$@"; baseline_dir="$2"; shift 2 ;;
    --base-url) require_value "$@"; base_url="${2%/}"; shift 2 ;;
    --readiness-path) require_value "$@"; readiness_path="$2"; shift 2 ;;
    --expected-shell) require_value "$@"; expected_shell="$2"; shift 2 ;;
    --workflow-path) require_value "$@"; workflow_path="$2"; shift 2 ;;
    --expected-status) require_value "$@"; expected_status="$2"; shift 2 ;;
    --expected-body) require_value "$@"; expected_body="$2"; shift 2 ;;
    --boots) require_value "$@"; boots="$2"; shift 2 ;;
    --output-json) require_value "$@"; output_json="$2"; shift 2 ;;
    --output-markdown) require_value "$@"; output_markdown="$2"; shift 2 ;;
    --artifacts-dir) require_value "$@"; artifacts_dir="$2"; shift 2 ;;
    --liveness-path) require_value "$@"; liveness_path="$2"; shift 2 ;;
    --startup-timeout-seconds) require_value "$@"; startup_timeout_seconds="$2"; shift 2 ;;
    --shutdown-timeout-seconds) require_value "$@"; shutdown_timeout_seconds="$2"; shift 2 ;;
    --retain-success-artifacts) retain_success_artifacts=true; shift ;;
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
[[ "$shutdown_timeout_seconds" =~ ^[1-9][0-9]*$ ]] || die_usage "--shutdown-timeout-seconds must be a positive integer"
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

content_root="$(cd "$content_root" && pwd)"
baseline_dir="$(cd "$baseline_dir" && pwd)"
server_dll="$(cd "$(dirname "$server_dll")" && pwd)/$(basename "$server_dll")"

read -r url_host url_port < <(python3 - "$base_url" <<'PY'
import sys
from urllib.parse import urlparse

value = urlparse(sys.argv[1])
if value.scheme not in ("http", "https") or value.hostname not in ("127.0.0.1", "localhost", "::1") or not value.port:
    raise SystemExit("--base-url must be an explicit loopback URL with a port")
print(value.hostname, value.port)
PY
) || die_usage "--base-url must be an explicit loopback URL with a port"

if ! python3 - "$url_host" "$url_port" <<'PY'
import socket, sys
try:
    family = socket.AF_INET6 if ':' in sys.argv[1] else socket.AF_INET
    with socket.socket(family) as sock:
        sock.bind((sys.argv[1], int(sys.argv[2])))
except OSError:
    raise SystemExit(1)
PY
then
  die_usage "Loopback port is occupied or unavailable: $url_host:$url_port"
fi

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
if [[ -z "$artifacts_dir" ]]; then
  temporary_parent="$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "${TMPDIR:-/tmp}")"
  if ! python3 - "$content_root" "$baseline_dir" "$temporary_parent" <<'PY'
import os, sys
for source in map(os.path.realpath, sys.argv[1:3]):
    destination = os.path.realpath(sys.argv[3])
    if os.path.commonpath((source, destination)) == source:
        raise SystemExit(1)
PY
  then
    die_usage "The temporary directory must not be inside a frozen input directory"
  fi
  artifacts_dir="$(mktemp -d "${TMPDIR:-/tmp}/elsa-cold-start-$timestamp-XXXXXX")"
else
  artifacts_dir="$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$artifacts_dir")"
  [[ ! -e "$artifacts_dir" ]] || die_usage "Artifacts directory already exists; choose a new path: $artifacts_dir"
fi
output_json="${output_json:-$artifacts_dir/report.json}"
output_markdown="${output_markdown:-$artifacts_dir/report.md}"
output_json="$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$output_json")"
output_markdown="$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$output_markdown")"
[[ "$output_json" != "$output_markdown" ]] || die_usage "JSON and Markdown report paths must be distinct"
if ! python3 - "$content_root" "$baseline_dir" "$artifacts_dir" "$output_json" "$output_markdown" <<'PY'
import os, sys
sources = [os.path.realpath(path) for path in sys.argv[1:3]]
destinations = [os.path.realpath(path) for path in sys.argv[3:]]
for source in sources:
    for destination in destinations:
        if os.path.commonpath((source, destination)) == source:
            print(f"source/destination paths overlap: {source} and {destination}", file=sys.stderr)
            raise SystemExit(1)
PY
then
  die_usage "Artifacts and reports must not overlap the frozen content or baseline directories"
fi
mkdir -p "$artifacts_dir"
mkdir -p "$(dirname "$output_json")" "$(dirname "$output_markdown")"
results_tsv="$artifacts_dir/boots.tsv"
printf 'boot\tlistening_ms\tactivation_ms\tshell_ready_ms\tfirst_request_ms\tfirst_success_ms\tshutdown_ms\tstatus\tlog_path\n' >"$results_tsv"

server_pid=""
server_session_ready=false
process_group_alive() {
  [[ -n "$server_pid" ]] && kill -0 -- "-$server_pid" 2>/dev/null
}

process_leader_alive() {
  [[ -n "$server_pid" ]] && kill -0 "$server_pid" 2>/dev/null
}

server_process_alive() {
  if [[ "$server_session_ready" == true ]]; then
    process_group_alive
  else
    process_leader_alive
  fi
}

signal_server_process() {
  local signal="$1"
  [[ -n "$server_pid" ]] || return 0

  if [[ "$server_session_ready" == true ]]; then
    kill "-$signal" -- "-$server_pid" 2>/dev/null || true
  else
    kill "-$signal" "$server_pid" 2>/dev/null || true
  fi
}

cleanup_process() {
  if server_process_alive; then
    signal_server_process TERM
    for ((attempt=0; attempt<shutdown_timeout_seconds*20; attempt++)); do
      server_process_alive || break
      sleep 0.05
    done
    if server_process_alive; then
      signal_server_process KILL
    fi
  fi
  [[ -n "$server_pid" ]] && wait "$server_pid" 2>/dev/null || true
  server_pid=""
  server_session_ready=false
}
trap cleanup_process EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

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

validate_readiness_body() {
  python3 - "$1" "$expected_shell" <<'PY'
import json, pathlib, sys
try:
    payload = json.loads(pathlib.Path(sys.argv[1]).read_text())
except (OSError, UnicodeError, json.JSONDecodeError):
    raise SystemExit(1)
if payload.get("status") != "ready" or payload.get("shell") != sys.argv[2]:
    raise SystemExit(1)
generation = payload.get("generation")
if not isinstance(generation, int) or isinstance(generation, bool) or generation < 1:
    raise SystemExit(1)
PY
}

wait_for_tcp() {
  local deadline_ns="$1"
  while :; do
    if python3 - "$url_host" "$url_port" <<'PY'
import socket, sys
try:
    with socket.create_connection((sys.argv[1], int(sys.argv[2])), timeout=.25):
        pass
except OSError:
    raise SystemExit(1)
PY
    then
      return 0
    fi
    if ! kill -0 "$server_pid" 2>/dev/null || (( $(now_ns) >= deadline_ns )); then
      return 1
    fi
    sleep 0.05
  done
}

hash_paths() {
  python3 - "$@" <<'PY'
import hashlib, pathlib, sys
h = hashlib.sha256()
for supplied in sys.argv[1:]:
    root = pathlib.Path(supplied)
    paths = sorted(p for p in (root.rglob('*') if root.is_dir() else [root]) if p.is_file())
    for path in paths:
        name = str(path.relative_to(root) if root.is_dir() else path.name).encode()
        h.update(len(name).to_bytes(8, 'big'))
        h.update(name)
        h.update(path.stat().st_size.to_bytes(8, 'big'))
        with path.open('rb') as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                h.update(chunk)
print(h.hexdigest())
PY
}

dotnet_version="$(dotnet --version)"
dotnet_runtimes="$(dotnet --list-runtimes | tr '\n' ';')"
machine="$(uname -a)"
repository_head="$(git -C "$(dirname "${BASH_SOURCE[0]}")/../.." rev-parse HEAD 2>/dev/null || printf unknown)"
baseline_hash="$(hash_paths "$baseline_dir")"
content_hash="$(hash_paths "$content_root")"
server_output_dir="$(dirname "$server_dll")"
server_hash="$(hash_paths "$server_output_dir")"
expected_body_file="$artifacts_dir/expected-body"
printf '%s' "$expected_body" >"$expected_body_file"
expected_body_hash="$(python3 - "$expected_body_file" <<'PY'
import hashlib, pathlib, sys
print(hashlib.sha256(pathlib.Path(sys.argv[1]).read_bytes()).hexdigest())
PY
)"

write_report() {
  local failure_boot="${1:-}" failure_category="${2:-}" failure_message="${3:-}" failure_log="${4:-}"
  python3 - "$results_tsv" "$output_json" "$output_markdown" "$repository_head" "$dotnet_version" \
    "$dotnet_runtimes" "$machine" "$server_hash" "$content_hash" "$baseline_hash" \
    "$expected_body_hash" "$expected_body_file" "$base_url" "$liveness_path" "$readiness_path" \
    "$workflow_path" "$expected_shell" "$expected_status" "$boots" \
    "$startup_timeout_seconds" "$shutdown_timeout_seconds" "$ready_budget_ms" "$workflow_budget_ms" \
    "$failure_boot" "$failure_category" "$failure_message" "$failure_log" <<'PY'
import csv, datetime, json, math, pathlib, sys

(tsv, json_path, markdown_path, repository_head, dotnet, runtimes, machine, server_hash,
 content_hash, baseline_hash, expected_body_hash, expected_body_artifact, base_url,
 liveness_path, readiness_path, workflow_path, expected_shell, expected_status, requested_boots,
 startup_timeout, shutdown_timeout, ready_budget, workflow_budget, failure_boot,
 failure_category, failure_message, failure_log) = sys.argv[1:]

metric_keys = (
    "listening_ms",
    "activation_ms",
    "shell_ready_ms",
    "first_request_ms",
    "first_success_ms",
    "shutdown_ms",
)
rows = []
with open(tsv, newline="") as stream:
    for row in csv.DictReader(stream, delimiter="\t"):
        rows.append({
            "boot": int(row["boot"]),
            **{key: float(row[key]) for key in metric_keys},
            "status": row["status"],
            "log_path": row["log_path"] or None,
            "logPath": row["log_path"] or None,
        })

if failure_category:
    rows.append({
        "boot": int(failure_boot),
        **{key: None for key in metric_keys},
        "status": failure_category,
        "log_path": failure_log,
        "logPath": failure_log,
    })

def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]

def aggregate(key):
    values = [row[key] for row in rows if row[key] is not None]
    if not values:
        return {"p50Ms": None, "p95Ms": None, "minMs": None, "maxMs": None}
    return {
        "p50Ms": percentile(values, .50),
        "p95Ms": percentile(values, .95),
        "minMs": min(values),
        "maxMs": max(values),
    }

aggregates = {key: aggregate(key) for key in metric_keys}
execution_complete = not failure_category

def budget(configured, actual):
    configured_ms = float(configured) if configured else None
    passed = None
    if configured_ms is not None and actual is not None and execution_complete:
        passed = actual <= configured_ms
    return {"configuredMs": configured_ms, "actualMs": actual, "passed": passed}

budgets = {
    "shellReadyP95Ms": budget(ready_budget, aggregates["shell_ready_ms"]["p95Ms"]),
    "firstRequestP95Ms": budget(workflow_budget, aggregates["first_request_ms"]["p95Ms"]),
}
configured_results = [item["passed"] for item in budgets.values() if item["configuredMs"] is not None]
budgets["passed"] = (
    all(configured_results)
    if execution_complete and configured_results and all(value is not None for value in configured_results)
    else None
)

budget_failures = []
if budgets["shellReadyP95Ms"]["passed"] is False:
    budget_failures.append(
        f"ready p95 {aggregates['shell_ready_ms']['p95Ms']:.3f} ms exceeds budget {float(ready_budget):.3f} ms")
if budgets["firstRequestP95Ms"]["passed"] is False:
    budget_failures.append(
        f"workflow p95 {aggregates['first_request_ms']['p95Ms']:.3f} ms exceeds budget {float(workflow_budget):.3f} ms")

failure = None
if failure_category:
    failure = {
        "boot": int(failure_boot),
        "category": failure_category,
        "message": failure_message,
        "logPath": failure_log,
    }
elif budget_failures:
    failure = {
        "boot": None,
        "category": "budget_failed",
        "message": "; ".join(budget_failures),
        "logPath": None,
    }

report = {
    "generatedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "provenance": {
        "repositoryHeadAtMeasurement": repository_head,
        "dotnetVersion": dotnet,
        "dotnetRuntimes": runtimes,
        "machine": machine,
        "serverOutputSha256": server_hash,
        "contentSha256": content_hash,
        "baselineSha256": baseline_hash,
        "expectedBodySha256": expected_body_hash,
        "expectedBodyArtifact": expected_body_artifact,
        "environment": "Production",
    },
    "request": {
        "baseUrl": base_url,
        "livenessPath": liveness_path,
        "readinessPath": readiness_path,
        "workflowPath": workflow_path,
        "expectedShell": expected_shell,
        "expectedStatus": int(expected_status),
        "boots": int(requested_boots),
        "startupTimeoutSeconds": int(startup_timeout),
        "shutdownTimeoutSeconds": int(shutdown_timeout),
    },
    "boots": rows,
    "aggregates": aggregates,
    "budgets": budgets,
    "outcome": {"status": "failed" if failure else "passed", "failure": failure},
}
pathlib.Path(json_path).write_text(json.dumps(report, indent=2) + "\n")

lines = [
    "# Elsa server cold-start report", "",
    f"- Boots: {sum(row['status'] == 'passed' for row in rows)}",
    f"- Requested boots: {requested_boots}",
    f"- Startup timeout: `{startup_timeout}` seconds",
    f"- Shutdown timeout: `{shutdown_timeout}` seconds",
    f"- Expected shell: `{expected_shell}`",
    f"- Expected workflow status: `{expected_status}`",
    f"- Expected body SHA-256: `{expected_body_hash}`",
    f"- Expected body artifact: `{expected_body_artifact}`",
    f"- Repository HEAD at measurement (not binary attribution): `{repository_head}`",
    f"- .NET SDK: `{dotnet}`",
    f"- Server output closure SHA-256: `{server_hash}`",
    f"- Content SHA-256: `{content_hash}`",
    f"- Baseline SHA-256: `{baseline_hash}`",
    f"- Machine: `{machine}`", "",
    "| Milestone | p50 (ms) | p95 (ms) | min (ms) | max (ms) |",
    "|---|---:|---:|---:|---:|",
]
labels = (
    ("listening_ms", "Listening"),
    ("activation_ms", "Activation"),
    ("shell_ready_ms", "Shell ready"),
    ("first_request_ms", "First workflow request"),
    ("first_success_ms", "First success"),
    ("shutdown_ms", "Shutdown"),
)
for key, label in labels:
    item = aggregates[key]
    cells = [item[name] for name in ("p50Ms", "p95Ms", "minMs", "maxMs")]
    rendered = ["n/a" if value is None else f"{value:.3f}" for value in cells]
    lines.append(f"| {label} | {' | '.join(rendered)} |")

lines.extend([
    "", "## Performance budgets", "",
    "| Budget | Configured (ms) | Actual (ms) | Result |",
    "|---|---:|---:|---|",
])
for key, label in (
    ("shellReadyP95Ms", "Shell ready p95"),
    ("firstRequestP95Ms", "First workflow request p95"),
):
    item = budgets[key]
    configured = "n/a" if item["configuredMs"] is None else f"{item['configuredMs']:.3f}"
    actual = "n/a" if item["actualMs"] is None else f"{item['actualMs']:.3f}"
    if item["configuredMs"] is None:
        result = "not configured"
    elif item["passed"] is None:
        result = "not evaluated"
    else:
        result = "passed" if item["passed"] else "failed"
    lines.append(f"| {label} | {configured} | {actual} | {result} |")

if failure:
    lines.extend(["", f"Failure: `{failure['category']}`. {failure['message']}"])
    if failure["logPath"]:
        lines.extend(["", f"Retained log: `{failure['logPath']}`"])
pathlib.Path(markdown_path).write_text("\n".join(lines) + "\n")

if budget_failures:
    print("Budget failed: " + "; ".join(budget_failures), file=sys.stderr)
    raise SystemExit(1)
PY
}

fail_boot() {
  local boot="$1" category="$2" message="$3" log_file="$4"
  write_report "$boot" "$category" "$message" "$log_file" || true
  printf 'ERROR: %s; retained log: %s; partial report: %s\n' "$message" "$log_file" "$output_json" >&2
  exit 1
}

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
  session_ready_file="$run_dir/session.ready"

  started_ns="$(now_ns)"
  launch_interrupted_status=""
  trap 'launch_interrupted_status=130' INT
  trap 'launch_interrupted_status=143' TERM
  (
    cd "$work_dir"
    exec python3 - "$base_url" "$server_dll" "$session_ready_file" <<'PY'
import os, pathlib, sys, time
test_delay = os.environ.get("_ELSA_PERF_TEST_PRE_SESSION_DELAY_SECONDS")
test_marker = os.environ.get("_ELSA_PERF_TEST_PRE_SESSION_MARKER")
if test_marker:
    pathlib.Path(test_marker).write_text(str(os.getpid()))
if test_delay:
    time.sleep(float(test_delay))
os.setsid()
pathlib.Path(sys.argv[3]).touch()
environment = os.environ.copy()
environment.pop("_ELSA_PERF_TEST_PRE_SESSION_DELAY_SECONDS", None)
environment.pop("_ELSA_PERF_TEST_PRE_SESSION_MARKER", None)
environment["ASPNETCORE_URLS"] = sys.argv[1]
environment["DOTNET_ENVIRONMENT"] = "Production"
os.execvpe("dotnet", ["dotnet", sys.argv[2]], environment)
PY
  ) >"$log_file" 2>&1 &
  server_pid=$!
  for ((attempt=0; attempt<startup_timeout_seconds*20; attempt++)); do
    [[ -f "$session_ready_file" ]] && break
    process_leader_alive || break
    sleep 0.05
  done
  if [[ -f "$session_ready_file" ]]; then
    server_session_ready=true
  fi
  trap 'exit 130' INT
  trap 'exit 143' TERM
  if [[ -n "$launch_interrupted_status" ]]; then
    exit "$launch_interrupted_status"
  fi
  if [[ "$server_session_ready" != true ]]; then
    fail_boot "$boot" "launch_failed" "Boot $boot did not establish an isolated server process group" "$log_file"
  fi
  deadline_ns=$((started_ns + startup_timeout_seconds * 1000000000))

  if ! wait_for_tcp "$deadline_ns"; then
    fail_boot "$boot" "listening_failed" "Boot $boot did not begin listening" "$log_file"
  fi
  listening_ns="$(now_ns)"
  live_deadline_ns=$((listening_ns + startup_timeout_seconds * 1000000000))
  if ! wait_for_status "$base_url$liveness_path" 200 "$live_deadline_ns" "$live_body"; then
    fail_boot "$boot" "liveness_failed" "Boot $boot did not pass liveness validation" "$log_file"
  fi
  deadline_ns=$(( $(now_ns) + startup_timeout_seconds * 1000000000 ))
  if ! wait_for_status "$base_url$readiness_path" 200 "$deadline_ns" "$ready_body"; then
    fail_boot "$boot" "readiness_failed" "Boot $boot did not become ready" "$log_file"
  fi
  if ! validate_readiness_body "$ready_body"; then
    fail_boot "$boot" "readiness_invalid" "Boot $boot returned malformed or unexpected readiness JSON" "$log_file"
  fi
  ready_ns="$(now_ns)"

  workflow_status="$(curl --silent --show-error --max-time "$startup_timeout_seconds" \
    --output "$workflow_body" --write-out '%{http_code}' "$base_url$workflow_path" || true)"
  workflow_ns="$(now_ns)"
  if [[ "$workflow_status" != "$expected_status" ]] || ! cmp -s "$expected_body_file" "$workflow_body"; then
    fail_boot "$boot" "workflow_validation_failed" \
      "Boot $boot workflow validation failed (status $workflow_status, expected $expected_status)" "$log_file"
  fi

  shutdown_started_ns="$(now_ns)"
  signal_server_process TERM
  for ((attempt=0; attempt<shutdown_timeout_seconds*20; attempt++)); do
    process_group_alive || break
    sleep 0.05
  done
  if process_group_alive; then
    signal_server_process KILL
    wait "$server_pid" 2>/dev/null || true
    fail_boot "$boot" "shutdown_failed" "Boot $boot process group did not shut down cleanly" "$log_file"
  fi
  wait "$server_pid" 2>/dev/null || true
  server_pid=""
  server_session_ready=false
  shutdown_ns="$(now_ns)"

  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\tpassed\t\n' \
    "$boot" "$(elapsed_ms "$started_ns" "$listening_ns")" \
    "$(elapsed_ms "$listening_ns" "$ready_ns")" "$(elapsed_ms "$started_ns" "$ready_ns")" \
    "$(elapsed_ms "$ready_ns" "$workflow_ns")" "$(elapsed_ms "$started_ns" "$workflow_ns")" \
    "$(elapsed_ms "$shutdown_started_ns" "$shutdown_ns")" >>"$results_tsv"

done

if ! write_report; then
  exit 1
fi

if [[ "$retain_success_artifacts" != true ]]; then
  for ((boot=1; boot<=boots; boot++)); do
    rm -rf "$artifacts_dir/boot-$(printf '%03d' "$boot")"
  done
fi

printf 'Cold-start report: %s\nMarkdown summary: %s\nRetained boot artifacts: %s\n' \
  "$output_json" "$output_markdown" "$artifacts_dir"

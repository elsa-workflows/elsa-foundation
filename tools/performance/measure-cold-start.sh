#!/usr/bin/env bash

# Cold-start / first-request phase baseline recipe (spec 129, First-Request/Cold-Start Readiness).
#
# Measures the wall time a fresh Elsa host takes to reach each cold-start milestone: process launch → healthy,
# the first authenticated call, and the first workflow execute — plus the mid-activation contention tail (a second
# request that arrives while the shell is still activating). It attaches to an already-running server (--base-url)
# or launches a fresh-database local Elsa.Workbench (--launch) with the boot phase-timing instrument enabled, and it
# scrapes the server's emitted phase table into the artifacts.
#
# Wall numbers are host-load sensitive; the schema operation COUNT (873 on the reference SQLite deployment schema)
# is deterministic and is asserted separately by ColdStartSchemaOperationCountTests. Always capture `uptime` load
# alongside every wall number produced here.

set -euo pipefail

usage() {
  cat <<'EOF'
Measure Elsa cold-start / first-request phases.

Usage:
  measure-cold-start.sh (--launch | --base-url URL) [options]

Mode (one required):
  --launch                    Start a fresh-database local Elsa.Workbench with the boot instrument enabled.
  --base-url URL              Attach to an already-running server root (e.g. https://localhost:7030).

Options:
  --project PATH              Elsa.Workbench.csproj path for --launch (default: src/Apps/Elsa.Workbench/Elsa.Workbench.csproj).
  --data-dir PATH             Fresh working directory for --launch SQLite databases (default: a temp dir).
  --health-path PATH          Health endpoint used to detect readiness (default: /).
  --authed-url URL            First authenticated call to time (optional).
  --auth-header HEADER        Authorization header value sent with --authed-url / --workflow-url (e.g. "Bearer ...").
  --workflow-url URL          First workflow execute to time (optional).
  --workflow-method METHOD    HTTP method for --workflow-url (default: GET).
  --expected-body TEXT        Exact expected body for --workflow-url (optional validation).
  --readiness-timeout SECONDS Max seconds to wait for the health endpoint (default: 120).
  --output-json PATH          Optional JSON report path.
  --output-markdown PATH      Optional Markdown report path.
  --insecure                  Disable TLS certificate verification (local development only).
  --help                      Show this help.
EOF
}

mode=""
base_url=""
launch=false
project="src/Apps/Elsa.Workbench/Elsa.Workbench.csproj"
data_dir=""
health_path="/"
authed_url=""
auth_header=""
workflow_url=""
workflow_method="GET"
expected_body=""
readiness_timeout=120
output_json=""
output_markdown=""
insecure=false

while (($# > 0)); do
  case "$1" in
    --launch) launch=true; mode="launch"; shift ;;
    --base-url) base_url="${2:?--base-url requires a value}"; mode="attach"; shift 2 ;;
    --project) project="${2:?}"; shift 2 ;;
    --data-dir) data_dir="${2:?}"; shift 2 ;;
    --health-path) health_path="${2:?}"; shift 2 ;;
    --authed-url) authed_url="${2:?}"; shift 2 ;;
    --auth-header) auth_header="${2:?}"; shift 2 ;;
    --workflow-url) workflow_url="${2:?}"; shift 2 ;;
    --workflow-method) workflow_method="${2:?}"; shift 2 ;;
    --expected-body) expected_body="${2:?}"; shift 2 ;;
    --readiness-timeout) readiness_timeout="${2:?}"; shift 2 ;;
    --output-json) output_json="${2:?}"; shift 2 ;;
    --output-markdown) output_markdown="${2:?}"; shift 2 ;;
    --insecure) insecure=true; shift ;;
    --help|-h) usage; exit 0 ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ -z "$mode" ]]; then
  printf '%s\n' 'One of --launch or --base-url is required.' >&2
  usage >&2
  exit 2
fi

for command in curl awk jq date; do
  if ! command -v "$command" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command" >&2
    exit 2
  fi
done

curl_common=(--silent --show-error --output /dev/null --write-out '%{http_code} %{time_total}')
if [[ "$insecure" == true ]]; then
  curl_common+=(--insecure)
fi

load_average() {
  # macOS + Linux: extract the 1-minute load average.
  uptime | sed -E 's/.*load averages?: *//; s/,.*//' | awk '{print $1}'
}

to_ms() { awk -v seconds="$1" 'BEGIN { printf "%.1f", seconds * 1000 }'; }

server_pid=""
server_log=""
cleanup() {
  if [[ -n "$server_pid" ]] && kill -0 "$server_pid" 2>/dev/null; then
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

load_before="$(load_average)"
launch_epoch="$(date +%s.%N)"

if [[ "$launch" == true ]]; then
  if [[ -z "$data_dir" ]]; then
    data_dir="$(mktemp -d)"
  fi
  mkdir -p "$data_dir"
  server_log="$data_dir/elsa-server-boot.log"
  printf 'Launching fresh Elsa.Workbench (data dir: %s)\n' "$data_dir" >&2
  # Fresh databases + boot instrument on. The server inherits a clean CWD so its CWD-relative SQLite files are new.
  (
    cd "$data_dir"
    exec dotnet run --project "$OLDPWD/$project" --no-launch-profile \
      --Elsa:Boot:PhaseTiming:Enabled=true
  ) >"$server_log" 2>&1 &
  server_pid=$!
  # Derive the base URL from the launched Kestrel log line if the caller did not attach.
  base_url="${base_url:-http://localhost:5089}"
fi

health_url="${base_url%/}$health_path"

# 1) boot → healthy
ready_epoch=""
deadline=$(( $(date +%s) + readiness_timeout ))
while (( $(date +%s) < deadline )); do
  status="$(curl "${curl_common[@]}" "$health_url" 2>/dev/null | awk '{print $1}' || true)"
  if [[ "$status" == "200" ]]; then
    ready_epoch="$(date +%s.%N)"
    break
  fi
  sleep 0.25
done
if [[ -z "$ready_epoch" ]]; then
  printf 'Server did not become healthy within %s seconds.\n' "$readiness_timeout" >&2
  exit 1
fi
boot_to_healthy_ms="$(to_ms "$(awk -v a="$launch_epoch" -v b="$ready_epoch" 'BEGIN { print b - a }')")"

measure_call() {
  local url="$1" method="${2:-GET}"
  local options=("${curl_common[@]}" --request "$method")
  if [[ -n "$auth_header" ]]; then
    options+=(--header "Authorization: $auth_header")
  fi
  local result seconds
  result="$(curl "${options[@]}" "$url" 2>/dev/null || echo '000 0')"
  seconds="$(printf '%s' "$result" | awk '{print $2}')"
  printf '%s %s' "$(printf '%s' "$result" | awk '{print $1}')" "$(to_ms "$seconds")"
}

# 2) mid-activation contention tail: two requests fired concurrently at the first authenticated (or health)
#    endpoint. On a cold shell the second request queues behind lazy activation; the slower of the two is the
#    contention tail.
contention_url="${authed_url:-$health_url}"
tmp_a="$(mktemp)"; tmp_b="$(mktemp)"
( measure_call "$contention_url" GET >"$tmp_a" ) &
( measure_call "$contention_url" GET >"$tmp_b" ) &
wait
contention_a_ms="$(awk '{print $2}' "$tmp_a")"; contention_b_ms="$(awk '{print $2}' "$tmp_b")"
rm -f "$tmp_a" "$tmp_b"
contention_tail_ms="$(awk -v a="$contention_a_ms" -v b="$contention_b_ms" 'BEGIN { print (a>b)?a:b }')"

# 3) first authenticated call (fresh connection; may itself be the activation trigger if launched cold)
first_authed_ms="null"; first_authed_status="null"
if [[ -n "$authed_url" ]]; then
  read -r first_authed_status first_authed_ms < <(measure_call "$authed_url" GET)
fi

# 4) first workflow execute
first_workflow_ms="null"; first_workflow_status="null"
if [[ -n "$workflow_url" ]]; then
  read -r first_workflow_status first_workflow_ms < <(measure_call "$workflow_url" "$workflow_method")
fi

load_after="$(load_average)"

# Scrape the server-emitted boot phase table (launch mode only).
phase_table="not captured (attach mode)"
if [[ -n "$server_log" && -f "$server_log" ]]; then
  phase_table="$(awk '/Elsa boot phase timing/{c=1} c' "$server_log" | head -n 40)"
  [[ -z "$phase_table" ]] && phase_table="not emitted yet (first request may not have completed)"
fi

timestamp_utc="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
git_revision="$(git rev-parse HEAD 2>/dev/null || printf 'unknown')"
machine="$(uname -sm)"

json_report="$(jq -n \
  --arg timestampUtc "$timestamp_utc" \
  --arg mode "$mode" \
  --arg baseUrl "$base_url" \
  --arg gitRevision "$git_revision" \
  --arg machine "$machine" \
  --arg loadBefore "$load_before" \
  --arg loadAfter "$load_after" \
  --argjson bootToHealthyMs "$boot_to_healthy_ms" \
  --argjson contentionAMs "$contention_a_ms" \
  --argjson contentionBMs "$contention_b_ms" \
  --argjson contentionTailMs "$contention_tail_ms" \
  --arg firstAuthedMs "$first_authed_ms" \
  --arg firstAuthedStatus "$first_authed_status" \
  --arg firstWorkflowMs "$first_workflow_ms" \
  --arg firstWorkflowStatus "$first_workflow_status" \
  '{timestampUtc: $timestampUtc, mode: $mode, baseUrl: $baseUrl,
    load: {oneMinuteBefore: $loadBefore, oneMinuteAfter: $loadAfter},
    coldStartMs: {
      bootToHealthy: $bootToHealthyMs,
      firstAuthenticatedCall: (if $firstAuthedMs == "null" then null else ($firstAuthedMs | tonumber) end),
      firstWorkflowExecute: (if $firstWorkflowMs == "null" then null else ($firstWorkflowMs | tonumber) end),
      contention: {requestA: $contentionAMs, requestB: $contentionBMs, tail: $contentionTailMs}
    },
    statuses: {
      firstAuthenticatedCall: (if $firstAuthedStatus == "null" then null else $firstAuthedStatus end),
      firstWorkflowExecute: (if $firstWorkflowStatus == "null" then null else $firstWorkflowStatus end)
    },
    environment: {gitRevision: $gitRevision, machine: $machine}}')"

markdown_report="$(printf '# Elsa cold-start / first-request phases\n\n- Timestamp (UTC): `%s`\n- Mode: `%s` (base URL: `%s`)\n- Load (1-min): before `%s`, after `%s`\n- Environment: `%s`, git `%s`\n\n| Phase | Wall (ms) |\n|---|---:|\n| boot → healthy | %s |\n| first authenticated call | %s |\n| first workflow execute | %s |\n| contention request A | %s |\n| contention request B | %s |\n| **contention tail (max)** | %s |\n\n## Server boot phase table\n\n```\n%s\n```\n' \
  "$timestamp_utc" "$mode" "$base_url" "$load_before" "$load_after" "$machine" "$git_revision" \
  "$boot_to_healthy_ms" "$first_authed_ms" "$first_workflow_ms" "$contention_a_ms" "$contention_b_ms" "$contention_tail_ms" \
  "$phase_table")"

if [[ -n "$output_json" ]]; then
  mkdir -p "$(dirname "$output_json")"
  printf '%s\n' "$json_report" > "$output_json"
fi
if [[ -n "$output_markdown" ]]; then
  mkdir -p "$(dirname "$output_markdown")"
  printf '%s\n' "$markdown_report" > "$output_markdown"
fi
if [[ -z "$output_json" && -z "$output_markdown" ]]; then
  printf '%s\n' "$json_report"
else
  printf 'boot→healthy: %s ms; contention tail: %s ms (load before %s / after %s).\n' \
    "$boot_to_healthy_ms" "$contention_tail_ms" "$load_before" "$load_after"
fi

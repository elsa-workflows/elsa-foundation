#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Measure a published Elsa synchronous HTTP workflow.

Usage:
  measure-http-workflow.sh --url URL --expected-body TEXT [options]

Required:
  --url URL                   Published workflow endpoint.
  --expected-body TEXT        Exact response body expected from every request.

Options:
  --warmup COUNT              Warm-up requests before measurement (default: 20).
  --requests COUNT            Warm requests to measure (default: 200).
  --expected-status CODE      Exact HTTP status expected (default: 200).
  --policy LABEL              Policy label recorded in the report.
  --segment-cap COUNT         Coalesced segment cap recorded in the report.
  --provider LABEL            Persistence provider label.
  --groundwork-db PATH        Optional SQLite database for commit-marker deltas.
  --output-json PATH          Optional JSON report path.
  --output-markdown PATH      Optional Markdown report path.
  --output-samples PATH       Write measured warm latencies, one numeric ms value per line.
  --enforce-p95-ms NUMBER     Fail when warm p95 exceeds this budget.
  --insecure                  Disable TLS certificate verification (local development only).
  --help                      Show this help.
EOF
}

url=""
expected_body=""
warmup=20
requests=200
expected_status=200
policy="unspecified"
segment_cap=""
provider="unspecified"
groundwork_db=""
output_json=""
output_markdown=""
output_samples=""
enforce_p95_ms=""
insecure=false

while (($# > 0)); do
  case "$1" in
    --url) url="${2:?--url requires a value}"; shift 2 ;;
    --expected-body) expected_body="${2:?--expected-body requires a value}"; shift 2 ;;
    --warmup) warmup="${2:?--warmup requires a value}"; shift 2 ;;
    --requests) requests="${2:?--requests requires a value}"; shift 2 ;;
    --expected-status) expected_status="${2:?--expected-status requires a value}"; shift 2 ;;
    --policy) policy="${2:?--policy requires a value}"; shift 2 ;;
    --segment-cap) segment_cap="${2:?--segment-cap requires a value}"; shift 2 ;;
    --provider) provider="${2:?--provider requires a value}"; shift 2 ;;
    --groundwork-db) groundwork_db="${2:?--groundwork-db requires a value}"; shift 2 ;;
    --output-json) output_json="${2:?--output-json requires a value}"; shift 2 ;;
    --output-markdown) output_markdown="${2:?--output-markdown requires a value}"; shift 2 ;;
    --output-samples)
      if [[ $# -lt 2 || -z "$2" ]]; then
        printf '%s\n' '--output-samples requires a value.' >&2
        exit 2
      fi
      output_samples="$2"
      shift 2
      ;;
    --enforce-p95-ms) enforce_p95_ms="${2:?--enforce-p95-ms requires a value}"; shift 2 ;;
    --insecure) insecure=true; shift ;;
    --help|-h) usage; exit 0 ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ -z "$url" || -z "$expected_body" ]]; then
  usage >&2
  exit 2
fi

if ! [[ "$warmup" =~ ^[0-9]+$ && "$requests" =~ ^[1-9][0-9]*$ && "$expected_status" =~ ^[1-5][0-9][0-9]$ ]]; then
  printf '%s\n' '--warmup must be non-negative, --requests positive, and --expected-status a three-digit HTTP status.' >&2
  exit 2
fi

for command in curl awk sort jq; do
  if ! command -v "$command" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command" >&2
    exit 2
  fi
done

if [[ -n "$groundwork_db" ]]; then
  if ! command -v sqlite3 >/dev/null 2>&1; then
    printf '%s\n' 'sqlite3 is required when --groundwork-db is supplied.' >&2
    exit 2
  fi
  if [[ ! -f "$groundwork_db" ]]; then
    printf 'Groundwork database not found: %s\n' "$groundwork_db" >&2
    exit 2
  fi
fi

if [[ -n "$segment_cap" ]] && ! [[ "$segment_cap" =~ ^[1-9][0-9]*$ ]]; then
  printf '%s\n' '--segment-cap must be positive when supplied.' >&2
  exit 2
fi

if [[ -n "$enforce_p95_ms" ]] && ! [[ "$enforce_p95_ms" =~ ^[0-9]+([.][0-9]+)?$ ]]; then
  printf '%s\n' '--enforce-p95-ms must be a non-negative number.' >&2
  exit 2
fi

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT
expected_file="$temporary_directory/expected-body"
body_file="$temporary_directory/response-body"
cold_file="$temporary_directory/cold-ms"
warm_file="$temporary_directory/warm-ms"
printf '%s' "$expected_body" > "$expected_file"

measure_request() {
  local destination="${1:-}"
  local measurement status seconds milliseconds
  local curl_options=(--silent --show-error --output "$body_file" --write-out '%{http_code} %{time_total}')
  if [[ "$insecure" == true ]]; then
    curl_options+=(--insecure)
  fi
  measurement="$(curl "${curl_options[@]}" "$url")"
  status="${measurement%% *}"
  seconds="${measurement#* }"

  if [[ "$status" != "$expected_status" ]]; then
    printf 'Unexpected HTTP status: expected %s, received %s.\n' "$expected_status" "$status" >&2
    exit 1
  fi
  if ! cmp -s "$expected_file" "$body_file"; then
    printf '%s\n' 'Response body did not match --expected-body.' >&2
    exit 1
  fi

  if [[ -n "$destination" ]]; then
    milliseconds="$(awk -v seconds="$seconds" 'BEGIN { printf "%.3f", seconds * 1000 }')"
    printf '%s\n' "$milliseconds" >> "$destination"
  fi
}

count_checkpoint_commits() {
  local snapshot="$temporary_directory/groundwork-snapshot-$1.db"
  sqlite3 "$groundwork_db" ".timeout 5000" ".backup '$snapshot'" >/dev/null
  sqlite3 "$snapshot" "SELECT COUNT(*) FROM groundwork_documents WHERE document_kind = 'checkpointCommit';"
}

percentile() {
  local sorted_file="$1"
  local percentile_value="$2"
  local count rank
  count="$(wc -l < "$sorted_file" | tr -d ' ')"
  rank="$(awk -v count="$count" -v percentile_value="$percentile_value" 'BEGIN { rank = int((count * percentile_value) + 0.999999); if (rank < 1) rank = 1; print rank }')"
  sed -n "${rank}p" "$sorted_file"
}

commits_before=""
if [[ -n "$groundwork_db" ]]; then
  commits_before="$(count_checkpoint_commits before)"
fi

measure_request "$cold_file"
for ((request = 0; request < warmup; request++)); do
  measure_request
done
for ((request = 0; request < requests; request++)); do
  measure_request "$warm_file"
done

sorted_file="$temporary_directory/warm-sorted-ms"
sort -n "$warm_file" > "$sorted_file"
cold_ms="$(head -n 1 "$cold_file")"
minimum_ms="$(head -n 1 "$sorted_file")"
maximum_ms="$(tail -n 1 "$sorted_file")"
average_ms="$(awk '{ total += $1 } END { printf "%.3f", total / NR }' "$warm_file")"
p50_ms="$(percentile "$sorted_file" 0.50)"
p95_ms="$(percentile "$sorted_file" 0.95)"
p99_ms="$(percentile "$sorted_file" 0.99)"

if [[ -n "$output_samples" ]]; then
  mkdir -p "$(dirname "$output_samples")"
  cp "$warm_file" "$output_samples"
fi

commits_after=""
commit_delta=""
if [[ -n "$groundwork_db" ]]; then
  commits_after="$(count_checkpoint_commits after)"
  commit_delta="$((commits_after - commits_before))"
fi

timestamp_utc="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
git_revision="$(git rev-parse HEAD 2>/dev/null || printf 'unknown')"
dotnet_version="$(dotnet --version 2>/dev/null || printf 'unavailable')"
machine="$(uname -sm)"

json_report="$(jq -n \
  --arg timestampUtc "$timestamp_utc" \
  --arg url "$url" \
  --arg policy "$policy" \
  --arg provider "$provider" \
  --arg segmentCap "$segment_cap" \
  --arg gitRevision "$git_revision" \
  --arg dotnetVersion "$dotnet_version" \
  --arg machine "$machine" \
  --argjson insecure "$insecure" \
  --argjson expectedStatus "$expected_status" \
  --argjson warmup "$warmup" \
  --argjson requests "$requests" \
  --argjson coldMs "$cold_ms" \
  --argjson minimumMs "$minimum_ms" \
  --argjson averageMs "$average_ms" \
  --argjson p50Ms "$p50_ms" \
  --argjson p95Ms "$p95_ms" \
  --argjson p99Ms "$p99_ms" \
  --argjson maximumMs "$maximum_ms" \
  --arg commitsBefore "$commits_before" \
  --arg commitsAfter "$commits_after" \
  --arg commitDelta "$commit_delta" \
  '{timestampUtc: $timestampUtc, endpoint: {url: $url, expectedStatus: $expectedStatus, tlsVerification: (if $insecure then "disabled" else "enabled" end)}, configuration: {policy: $policy, segmentCap: (if $segmentCap == "" then null else ($segmentCap | tonumber) end), provider: $provider}, samples: {warmup: $warmup, measured: $requests}, latencyMs: {cold: $coldMs, min: $minimumMs, mean: $averageMs, p50: $p50Ms, p95: $p95Ms, p99: $p99Ms, max: $maximumMs}, physicalCheckpointCommits: (if $commitDelta == "" then null else {before: ($commitsBefore | tonumber), after: ($commitsAfter | tonumber), delta: ($commitDelta | tonumber)} end), environment: {gitRevision: $gitRevision, dotnetVersion: $dotnetVersion, machine: $machine}}')"

markdown_report="$(printf '# Elsa HTTP workflow performance\n\n- Timestamp (UTC): `%s`\n- Endpoint: `%s` (TLS verification: `%s`)\n- Policy: `%s` (segment cap: `%s`)\n- Provider: `%s`\n- Samples: 1 cold, %s warm-up, %s measured\n- Latency (ms): cold `%s`, min `%s`, mean `%s`, p50 `%s`, p95 `%s`, p99 `%s`, max `%s`\n- Physical checkpoint commits: `%s`\n- Environment: `%s`, .NET `%s`, git `%s`\n' \
  "$timestamp_utc" "$url" "$(if [[ "$insecure" == true ]]; then printf disabled; else printf enabled; fi)" "$policy" "${segment_cap:-n/a}" "$provider" "$warmup" "$requests" \
  "$cold_ms" "$minimum_ms" "$average_ms" "$p50_ms" "$p95_ms" "$p99_ms" "$maximum_ms" \
  "${commit_delta:-not measured}" "$machine" "$dotnet_version" "$git_revision")"

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
  printf 'Measured warm p95: %s ms (cold: %s ms).\n' "$p95_ms" "$cold_ms"
fi

if [[ -n "$enforce_p95_ms" ]] && ! awk -v actual="$p95_ms" -v budget="$enforce_p95_ms" 'BEGIN { exit !(actual <= budget) }'; then
  printf 'Warm p95 %s ms exceeds budget %s ms.\n' "$p95_ms" "$enforce_p95_ms" >&2
  exit 1
fi

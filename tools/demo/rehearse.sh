#!/usr/bin/env bash
# Runs tools/demo/RUNBOOK.md end to end without anyone at the keyboard: Act 1 on Sqlite (one host, upgraded in place) and Act 2 on
# PostgreSQL (two hosts sharing a database, each upgraded in place). Every status code and output line the runbook promises is
# asserted; the timing of every step is printed at the end; the hosts and the container are removed on exit, and the host logs
# are kept in artifacts/demo-rehearsal.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat >&2 <<'USAGE'
Usage: bash tools/demo/rehearse.sh [--act 1|2] [--keep]

Needs the releases staged (bash tools/demo/prepack.sh), Docker with the cached postgres:16-alpine image for Act 2, curl, and
python3. It starts with tools/demo/reset.sh, so it stops any demo host of this checkout, and it ends the same way.

  --act 1|2   rehearse one act only
  --keep      do not clean up on exit: the hosts keep running and the container stays, for looking around (stop them with reset.sh)

Ports: DEMO_PORT_SOLO (5101), DEMO_PORT_A (5201), DEMO_PORT_B (5202); the container: DEMO_PG_CONTAINER and DEMO_PG_PORT.
The exit status is 0 only when every assertion held.
USAGE
  exit 2
}

acts="1 2"
keep=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --act) [[ "${2:-}" == "1" || "${2:-}" == "2" ]] || usage; acts="$2"; shift 2 ;;
    --keep) keep=1; shift ;;
    -h|--help) usage ;;
    *) echo "unknown argument: $1" >&2; usage ;;
  esac
done

demo_require_python
cd "$demo_root"

port_solo="${DEMO_PORT_SOLO:-5101}"
port_a="${DEMO_PORT_A:-5201}"
port_b="${DEMO_PORT_B:-5202}"
logs="$demo_root/artifacts/demo-rehearsal"
export DEMO_KEY="rehearsal-key"
# The database of Act 1 is the default Sqlite file, so nothing here may point ELSA_EF_CONNECTION anywhere else; Act 2 sets it
# for the commands that need it (with_pg), and only for them.
unset ELSA_EF_CONNECTION

sqlite_target=(--environment Development --provider Sqlite --modules Samples.Notes)
pg_target=(--environment Development --provider PostgreSql --modules Samples.Notes,Cluster.Membership)
status_fast=(--skew-allowance 00:00:02)
solo_dir="artifacts/demo/hosts/solo"
a_dir="artifacts/demo/hosts/a"
b_dir="artifacts/demo/hosts/b"

# ---------------------------------------------------------------------------------------------------------------- reporting

now() { python3 -c 'import time; print(f"{time.time():.3f}")'; }
timings=()
step_name=""
step_started=""
begun="$(now)"

# step LABEL: ends the step in progress (recording its time) and starts the next.
step() {
  end_step
  step_name="$1"
  step_started="$(now)"
  echo
  echo "--- $step_name"
}
end_step() {
  [[ -z "$step_name" ]] || timings+=("$(python3 -c "import sys; print(f'{float(sys.argv[1]) - float(sys.argv[2]):7.1f} s  {sys.argv[3]}')" "$(now)" "$step_started" "$step_name")")
  step_name=""
}

ok() { echo "    ok: $*"; }

# Where the failure is, then what the hosts said: their warnings and errors, and the end of each log.
fail() {
  echo "    FAILED: $*" >&2
  for log in "$logs"/*.log; do
    [[ -e "$log" ]] || continue
    echo "    ---- $(basename "$log"): warnings and errors" >&2
    grep -E "^(warn|fail|crit)" -A1 "$log" | cut -c1-300 | tail -n 24 >&2 || true
    echo "    ---- $(basename "$log"): last 15 lines" >&2
    tail -n 15 "$log" | cut -c1-300 >&2
  done
  exit 1
}

cleanup() {
  local status=$?
  end_step
  echo
  echo "=== timings"
  printf '%s\n' "${timings[@]}"
  echo "  total $(python3 -c "import sys; print(f'{float(sys.argv[1]) - float(sys.argv[2]):.0f}')" "$(now)" "$begun") s"
  if [[ "$keep" -eq 1 ]]; then
    echo "=== --keep: hosts and container left running; bash tools/demo/reset.sh stops and removes them"
  else
    echo "=== cleaning up"
    bash tools/demo/reset.sh || echo "cleanup failed: run bash tools/demo/reset.sh" >&2
  fi
  [[ $status -eq 0 ]] && echo "=== REHEARSAL PASSED" || echo "=== REHEARSAL FAILED (exit $status); host logs are in ${logs#"$demo_root"/}"
  exit $status
}
trap cleanup EXIT

# ------------------------------------------------------------------------------------------------------------- doing things

out=""
# run COMMAND...: prints the command and the head of its output, keeps the output in $out, and fails the rehearsal on a non-zero exit.
run() {
  echo "  \$ $*"
  local status=0
  out="$("$@" 2>&1)" || status=$?
  if [[ -n "$out" ]]; then printf '%s\n' "$out" | cut -c1-240 | head -n 14 | sed 's/^/      /'; fi
  [[ "$status" -eq 0 ]] || fail "exited $status: $*"
}

# The connection of Act 2 reaches a command in its environment and is neither printed nor written anywhere.
with_pg() { ELSA_EF_CONNECTION="$demo_pg_connection" "$@"; }

expect_has() { # DESCRIPTION TEXT NEEDLE
  printf '%s' "$2" | grep -qF -- "$3" || fail "$1: expected to find '$3' in: $(printf '%s' "$2" | cut -c1-600)"
  ok "$1 (found '$3')"
}
expect_eq() { # DESCRIPTION EXPECTED ACTUAL
  [[ "$2" == "$3" ]] || fail "$1: expected $2, got $3 (body: $(cut -c1-500 "$logs/body"))"
  ok "$1 ($3)"
}

code=""
# request METHOD PORT PATH [JSON]: the status code goes to $code and the body to $logs/body and $body.
body=""
request() {
  local method="$1" port="$2" path="$3" data="${4:-}" args=()
  args=(-s -m 120 -o "$logs/body" -w '%{http_code}' -X "$method" -H "X-Elsa-Module-Management-Key: $DEMO_KEY")
  [[ -z "$data" ]] || args+=(-H 'content-type: application/json' -d "$data")
  code="$(curl "${args[@]}" "http://127.0.0.1:$port$path")" || fail "curl $method $path on port $port failed"
  body="$(<"$logs/body")"
  echo "  \$ curl -X $method :$port$path -> $code $(printf '%s' "$body" | cut -c1-150)"
}
reload() { request POST "$1" /_module-management/reload; }

# wait_until DESCRIPTION SECONDS COMMAND...: polls once a second.
wait_until() {
  local description="$1" limit="$2" waited=0
  shift 2
  until "$@"; do
    [[ "$waited" -lt "$limit" ]] || fail "gave up after $limit s waiting for: $description"
    sleep 1
    waited=$((waited + 1))
  done
  ok "$description (after $waited s)"
}

# start_host NAME PORT [run-host.sh arguments]: the host runs in the background, its output in its own log.
start_host() {
  local name="$1" port="$2"
  shift 2
  echo "  \$ bash tools/demo/run-host.sh $name --port $port $*   (log: ${logs#"$demo_root"/}/$name.log)"
  bash tools/demo/run-host.sh "$name" --port "$port" --management-key-env DEMO_KEY "$@" >"$logs/$name.log" 2>&1 &
  echo "$!" >"$logs/$name.job"
}
ready() { # NAME PORT: the script becomes the host, so the job's process id is the host's
  kill -0 "$(<"$logs/$1.job")" 2>/dev/null || fail "host $1 is not running"
  [[ "$(curl -s -o /dev/null -w '%{http_code}' -m 5 "http://127.0.0.1:$2/health/ready" || true)" == "200" ]]
}
wait_ready() { wait_until "host $1 ready" 300 ready "$1" "$2"; }
refusals() { grep -c "was refused" "$logs/$1.log" || true; }
refused_again() { [[ "$(refusals "$1")" -gt "$2" ]]; } # NAME COUNT-BEFORE

# publish RELEASE HOST: the demo's copy into the feed, then the wait for the host to have installed it and refused the shell.
publish_and_wait_for_refusal() {
  local release="$1" name="$2" before
  before="$(refusals "$name")"
  run bash tools/demo/publish.sh "$release" --host "$name"
  wait_until "host $name installed the release and refused the reload" 180 refused_again "$name" "$before"
}

# ------------------------------------------------------------------------------------------------------------------- start

mkdir -p "$logs"
step "Clean state"
[[ -f "$demo_staging/1/Elsa.Samples.Nuplane.Notes.1.0.0.nupkg" && -f "$demo_staging/2/Elsa.Samples.Nuplane.Notes.1.1.0.nupkg" ]] ||
  demo_fail "The releases are not staged. Run: bash tools/demo/prepack.sh"
run bash tools/demo/reset.sh
rm -rf "$logs"
mkdir -p "$logs"
# The containers and ports the acts need must be free, or the rehearsal would test somebody else's.
for port in "$port_solo" "$port_a" "$port_b"; do
  ! (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null || fail "port $port is in use; set DEMO_PORT_SOLO, DEMO_PORT_A and DEMO_PORT_B"
done

# ---------------------------------------------------------------------------------------------------------------------- setup
# Everything of the runbook's setup, for both acts, before either is shown: the hosts are started one after the other.

if [[ " $acts " == *" 1 "* ]]; then
  step "Setup: host solo (Sqlite): publish 1.0.0, restore, apply"
  run bash tools/demo/publish.sh 1 --host solo
  run bash tools/demo/run-host.sh solo --port "$port_solo" --management-key-env DEMO_KEY --prepare-only
  run bash tools/demo/elsa.sh persistence apply --restore --host "$solo_dir" "${sqlite_target[@]}"
  expect_has "the initial migration is applied" "$out" "Samples.Notes"
  step "Setup: host solo started"
  start_host solo "$port_solo"
  wait_ready solo "$port_solo"
fi

if [[ " $acts " == *" 2 "* ]]; then
  step "Setup: PostgreSQL container"
  run docker run -d --name "$demo_pg_container" -e POSTGRES_PASSWORD=demo -e POSTGRES_DB=elsa -p "127.0.0.1:$demo_pg_port:5432" postgres:16-alpine
  pg_up() { docker exec "$demo_pg_container" pg_isready -q -h 127.0.0.1 -U postgres -d elsa; }
  wait_until "PostgreSQL accepts connections" 90 pg_up

  step "Setup: hosts a and b (PostgreSQL): publish 1.0.0, restore, apply"
  run bash tools/demo/publish.sh 1 --host a
  run bash tools/demo/publish.sh 1 --host b
  run with_pg bash tools/demo/run-host.sh a --port "$port_a" --provider PostgreSql --cluster host-a --fast-membership --management-key-env DEMO_KEY --prepare-only
  run with_pg bash tools/demo/elsa.sh persistence apply --restore --host "$a_dir" "${pg_target[@]}"
  expect_has "both modules are applied" "$out" "Cluster.Membership"
  run with_pg bash tools/demo/run-host.sh b --port "$port_b" --provider PostgreSql --cluster host-b --fast-membership --management-key-env DEMO_KEY --prepare-only

  step "Setup: host a started, then host b"
  with_pg start_host a "$port_a" --provider PostgreSql --cluster host-a --fast-membership
  wait_ready a "$port_a"
  with_pg start_host b "$port_b" --provider PostgreSql --cluster host-b --fast-membership
  wait_ready b "$port_b"
fi

# ------------------------------------------------------------------------------------------------------------------------ Act 1

if [[ " $acts " == *" 1 "* ]]; then
  step "Act 1.1 v1 running: add and list notes"
  request POST "$port_solo" /demo/notes '{"text":"hello"}'
  expect_eq "adding a note" 200 "$code"
  note_id="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$logs/body")"
  request GET "$port_solo" /demo/notes
  expect_eq "listing notes" 200 "$code"
  expect_has "the note is listed" "$body" '"text":"hello"'
  request GET "$port_solo" /demo/notes/with-tags
  expect_eq "with-tags does not exist in 1.0.0" 404 "$code"

  step "Act 1.2 show the change"
  run bash tools/demo/show-change.sh
  expect_has "the V2 folder is shown" "$out" "The V2 folder"
  full_change="$(bash tools/demo/show-change.sh)"
  expect_has "the tags endpoint is in the change" "$full_change" "NoteStore.Tags.cs"
  expect_has "the migration is in the change" "$full_change" "AddTags"
  expect_has "the package version is shown" "$full_change" "1.1.0"

  step "Act 1.3-4 publish 1.1.0; the host installs it; /reload is refused (409)"
  publish_and_wait_for_refusal 2 solo
  reload "$port_solo"
  expect_eq "reload is refused" 409 "$code"
  expect_has "the refusal names the module" "$body" '"module":"Samples.Notes"'
  expect_has "the refusal names the migration" "$body" "AddTags"
  expect_has "the refusal names the apply command" "$body" "dotnet elsa persistence apply"
  request GET "$port_solo" /demo/notes
  expect_eq "the host keeps serving 1.0.0 meanwhile" 200 "$code"

  step "Act 1.5 dotnet elsa persistence apply"
  run bash tools/demo/elsa.sh persistence apply --host "$solo_dir" "${sqlite_target[@]}"
  expect_has "the apply names the module" "$out" "Samples.Notes"

  step "Act 1.6 /reload answers 200; with-tags works; old notes show tags []"
  reload "$port_solo"
  expect_eq "reload succeeds" 200 "$code"
  expect_has "one shell reloaded" "$body" '"reloaded":1'
  request GET "$port_solo" /demo/notes/with-tags
  expect_eq "with-tags answers" 200 "$code"
  expect_has "the old note has no tags" "$body" '"tags":[]'
  request POST "$port_solo" "/demo/notes/$note_id/tags" '{"tags":["demo"]}'
  expect_eq "tagging a note" 200 "$code"
  expect_has "the tag is on the note" "$body" '"demo"'
  request GET "$port_solo" /demo/notes
  expect_eq "the base endpoint still works" 200 "$code"
fi

# ------------------------------------------------------------------------------------------------------------------------ Act 2

if [[ " $acts " == *" 2 "* ]]; then
  step "Act 2.1 both hosts on 1.0.0"
  request POST "$port_a" /demo/notes '{"text":"written on a"}'
  expect_eq "note on a" 200 "$code"
  request POST "$port_b" /demo/notes '{"text":"written on b"}'
  expect_eq "note on b" 200 "$code"
  request GET "$port_a" /demo/notes
  expect_has "a sees the note written on b" "$body" "written on b"
  run with_pg bash tools/demo/elsa.sh persistence status --host "$a_dir" "${pg_target[@]}" "${status_fast[@]}" --family SamplesNotes
  expect_has "status lists host-a" "$out" "host-a"
  expect_has "status lists host-b" "$out" "host-b"

  step "Act 2.2 upgrade b in place: publish, refused (409), apply, reload (200)"
  publish_and_wait_for_refusal 2 b
  reload "$port_b"
  expect_eq "reload of b is refused" 409 "$code"
  expect_has "the refusal names the module" "$body" '"module":"Samples.Notes"'
  expect_has "the refusal names the apply command" "$body" "dotnet elsa persistence apply"
  run with_pg bash tools/demo/elsa.sh persistence apply --host "$b_dir" "${pg_target[@]}"
  reload "$port_b"
  expect_eq "reload of b succeeds" 200 "$code"

  step "Act 2.3 with-tags on b: 409 until every host can read 2.0.0"
  request GET "$port_b" /demo/notes/with-tags
  expect_eq "with-tags on b is held back" 409 "$code"
  expect_has "the reason" "$body" "every host can read version '2.0.0'"
  request POST "$port_a" /demo/notes '{"text":"a, still 1.0.0"}'
  expect_eq "a keeps writing" 200 "$code"

  step "Act 2.4 persistence status names host-a"
  run with_pg bash tools/demo/elsa.sh persistence status --host "$b_dir" "${pg_target[@]}" "${status_fast[@]}" --family SamplesNotes
  expect_has "2.0.0 waits for host-a" "$out" "waits for: host-a (reads 1.0.0)"
  expect_has "finalized still at 1.0.0" "$out" "finalized at 1.0.0"

  step "Act 2.5 upgrade a in place"
  run bash tools/demo/publish.sh 2 --host a
  wait_until "host a installed the release" 180 grep -q "Loaded package Elsa.Samples.Nuplane.Notes@1.1.0" "$logs/a.log"
  reload "$port_a"
  echo "    (a is on a migrated database already; the reload answered $code)"
  [[ "$code" == "200" || "$code" == "409" ]] || fail "reload of a answered $code"

  step "Act 2.6 the version finalizes: both answer 200, status says finalized at 2.0.0"
  finalized_on_a() { [[ "$(curl -s -o /dev/null -w '%{http_code}' -m 20 "http://127.0.0.1:$port_a/demo/notes/with-tags" || true)" == "200" ]]; }
  finalized_on_b() { [[ "$(curl -s -o /dev/null -w '%{http_code}' -m 20 "http://127.0.0.1:$port_b/demo/notes/with-tags" || true)" == "200" ]]; }
  wait_until "with-tags answers 200 on b" 120 finalized_on_b
  wait_until "with-tags answers 200 on a" 120 finalized_on_a
  request GET "$port_a" /demo/notes/with-tags
  expect_has "a's old notes have no tags" "$body" '"tags":[]'
  complete_at_2() { with_pg bash tools/demo/elsa.sh persistence status --host "$a_dir" "${pg_target[@]}" "${status_fast[@]}" --family SamplesNotes >"$logs/status.txt" 2>&1 && grep -q "complete from 2.0.0" "$logs/status.txt"; }
  wait_until "status says complete from 2.0.0" 120 complete_at_2
  status_text="$(<"$logs/status.txt")"
  expect_has "finalized at 2.0.0" "$status_text" "finalized at 2.0.0"
  printf '%s\n' "$status_text" | sed 's/^/      /'
fi

end_step

#!/usr/bin/env bash
# Runs tools/demo/RUNBOOK.md end to end without anyone at the keyboard: Act 1 on Sqlite (one host, upgraded in place) and Act 2 on
# PostgreSQL (two hosts sharing a database, each upgraded in place). What the presenter types is what runs here: the helpers
# (note, withtags, reload, tag, status, waitfor, rows, pgconn's connection) come from tools/demo/helpers.sh, the file the runbook
# sources, and the status codes and output lines the runbook promises are asserted on their output. The timing of every step is
# printed at the end; the hosts and the container are removed on exit, and the host logs are kept in artifacts/demo-rehearsal.
# Not rehearsed: prepack.sh, which takes minutes and which this script only requires to have run.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/rehearse.sh [--act 1|2] [--fallback] [--keep]

Needs the releases staged (bash tools/demo/prepack.sh), curl, jq and python3, and Docker with the cached postgres:16-alpine image
for Act 2. It starts with tools/demo/reset.sh, so it stops any demo host of this checkout, and it ends the same way.

  --act 1|2   rehearse one act only
  --fallback  Act 1 by the fallback route of the runbook: stop the host, publish 1.1.0, start it again
  --keep      do not clean up on exit: the hosts keep running and the container stays, for looking around (stop them with reset.sh)

Ports: DEMO_PORT_SOLO (5101), DEMO_PORT_A (5201), DEMO_PORT_B (5202); the container: DEMO_PG_CONTAINER. The container's own
port is a free one that Docker picks.
The exit status is 0 only when every assertion held.
USAGE
}

acts="1 2"
keep=0
fallback=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --act) [[ "${2:-}" == "1" || "${2:-}" == "2" ]] || demo_fail "--act is 1 or 2, not '${2:-}' (see --help)."; acts="$2"; shift 2 ;;
    --keep) keep=1; shift ;;
    --fallback) fallback=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) demo_fail "unknown argument '$1' (see --help)." ;;
  esac
done

demo_require curl jq
demo_require_python
[[ " $acts " != *" 2 "* ]] || demo_require docker
cd "$demo_root"

# The database of Act 1 is the default Sqlite file, so nothing here may point ELSA_EF_CONNECTION anywhere else; Act 2 sets it
# for the commands that need it (with_pg), and only for them.
unset ELSA_EF_CONNECTION
# shellcheck source=helpers.sh
source tools/demo/helpers.sh

port_solo="${DEMO_PORT_SOLO:-5101}"
port_a="${DEMO_PORT_A:-5201}"
port_b="${DEMO_PORT_B:-5202}"
logs="$demo_root/artifacts/demo-rehearsal"

sqlite_target=(--environment Development --provider Sqlite --modules Samples.Notes)
solo_dir="artifacts/demo/hosts/solo"
a_dir="artifacts/demo/hosts/a"
b_dir="artifacts/demo/hosts/b"
pg_target=(--environment Development --provider PostgreSql --modules "Samples.Notes,Cluster.Membership")

# ---------------------------------------------------------------------------------------------------------------- reporting

now() { python3 -c 'import time; print(f"{time.time():.3f}")'; }
# elapsed SINCE: seconds since a timestamp of now(), one decimal.
elapsed() { python3 -c "import sys; print(f'{float(sys.argv[1]) - float(sys.argv[2]):.1f}')" "$(now)" "$1"; }

timings=()
measures=()
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
  [[ -z "$step_name" ]] || timings+=("$(printf '%7s s  %s' "$(elapsed "$step_started")" "$step_name")")
  step_name=""
}

measure() { measures+=("$*"); echo "    measured: $*"; }

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
  printf '%s\n' ${timings[@]+"${timings[@]}"}
  if [[ ${#measures[@]} -gt 0 ]]; then
    echo "=== measured"
    printf '  %s\n' "${measures[@]}"
  fi
  echo "  total $(printf '%.0f' "$(elapsed "$begun")") s"
  echo "=== screen hygiene: lines that show an internal spec or FR number"
  echo "  in command and HTTP output: $(grep -hE '[Ss]pec [0-9]+|FR-[0-9]+' "$logs/screen.txt" 2>/dev/null | sort -u | wc -l | tr -d ' ')"
  echo "  in host logs:"
  grep -hE '[Ss]pec [0-9]+|FR-[0-9]+' "$logs"/*.log 2>/dev/null | cut -c1-200 | sort | uniq -c | sed 's/^/    /' || true
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
# A signal must not read as success: without these the cleanup would see the status of the last command that finished.
trap 'exit 130' INT
trap 'exit 143' TERM

# ------------------------------------------------------------------------------------------------------------- doing things

out=""
# run COMMAND...: prints the command and the head of its output, keeps the whole output in $out, and fails the rehearsal on a
# non-zero exit. The command may be one of the presenter's helpers.
run() {
  echo "  \$ $*"
  local status=0
  out="$("$@" 2>&1)" || status=$?
  printf '%s\n' "$out" >>"$logs/screen.txt"
  if [[ -n "$out" ]]; then printf '%s\n' "$out" | cut -c1-240 | head -n 14 | sed 's/^/      /'; fi
  [[ "$status" -eq 0 ]] || fail "exited $status: $*"
}
# stage COMMAND...: run, for what the audience sees: its output is also kept in stage.txt, which is checked for absolute paths.
stage() {
  run "$@"
  printf '%s\n' "$out" >>"$logs/stage.txt"
}

# The connection of Act 2 reaches a command in its environment and is neither printed nor written anywhere.
with_pg() { ELSA_EF_CONNECTION="$demo_pg_connection" "$@"; }
# pg_status HOST: the presenter's status HOST, against the PostgreSQL database.
pg_status() { stage with_pg status "$1"; }
# apply_solo: Act 1's persistence apply of the pending migration, as the 409 asks for it.
apply_solo() { stage bash tools/demo/elsa.sh persistence apply --host "$solo_dir" "${sqlite_target[@]}"; }
# http_code URL: the status code, 000 when nothing answers.
http_code() { curl -s -o /dev/null -w '%{http_code}' -m 20 "$1" || true; }

first_line() { printf '%s\n' "$1" | head -n 1; }
line_count() { printf '%s\n' "$1" | wc -l | tr -d ' '; }

expect_has() { # DESCRIPTION TEXT NEEDLE
  printf '%s' "$2" | grep -qF -- "$3" || fail "$1: expected to find '$3' in: $(printf '%s' "$2" | cut -c1-600)"
  ok "$1 (found '$3')"
}
expect_match() { # DESCRIPTION TEXT EXTENDED-REGEX
  printf '%s\n' "$2" | grep -qE -- "$3" || fail "$1: expected a line matching '$3' in: $(printf '%s' "$2" | cut -c1-600)"
  ok "$1 (matched '$3')"
}
expect_eq() { # DESCRIPTION EXPECTED ACTUAL
  [[ "$2" == "$3" ]] || fail "$1: expected '$2', got '$3'"
  ok "$1 ($3)"
}

ready() { # NAME PORT: the script becomes the host, so the job's process id is the host's
  kill -0 "$(<"$logs/$1.job")" 2>/dev/null || fail "host $1 is not running"
  [[ "$(http_code "http://127.0.0.1:$2/health/ready")" == "200" ]]
}
with_tags_ok() { [[ "$(http_code "http://127.0.0.1:$1/demo/notes/with-tags")" == "200" ]]; }
refusals() { grep -c "was refused" "$logs/$1.log" || true; }
refused_again() { [[ "$(refusals "$1")" -gt "$2" ]]; } # NAME COUNT-BEFORE
# no_tags_on OUTPUT: every note after the status line reads as untagged, and there is at least one.
no_tags_on() {
  [[ "$(line_count "$1")" -gt 1 ]] || fail "expected notes after the status line in: $1"
  expect_eq "no note carries a tag" 0 "$(printf '%s\n' "$1" | tail -n +2 | grep -vc '"tags":\[\]' || true)"
}

# notes_in OUTPUT SCHEMA TAGS: how many of the notes that rows printed carry that schema version (a literal) and that tags value
# (an extended regex: NULL, \[\], ...), the header line left out. The column is padded by two spaces at least, which is what tells
# it from a note whose text ends in a version.
notes_in() { printf '%s\n' "$1" | tail -n +2 | grep -cE "[ ]{2}$2 +$3\$" || true; }
# note_count OUTPUT: how many notes rows printed.
note_count() { echo $(($(line_count "$1") - 1)); }

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
wait_ready() { wait_until "host $1 ready" 300 ready "$1" "$2"; }

# publish_and_wait_for_refusal RELEASE HOST: the demo's copy into the feed, then the wait for the host to have installed it and refused the shell.
publish_and_wait_for_refusal() {
  local release="$1" name="$2" before
  before="$(refusals "$name")"
  stage bash tools/demo/publish.sh "$release" --host "$name"
  expect_has "the publish line" "$out" "published Elsa.Samples.Nuplane.Notes.$(demo_release_version "$release").nupkg to artifacts/demo/hosts/$name/feed"
  wait_until "host $name installed the release and refused the reload" 180 refused_again "$name" "$before"
}

# ------------------------------------------------------------------------------------------------------------------- start

mkdir -p "$logs"
step "Clean state"
for release in 1 2; do
  [[ -f "$(demo_staged_package "$release")" ]] || demo_fail "The releases are not staged. Run: bash tools/demo/prepack.sh"
done
run bash tools/demo/reset.sh
rm -rf "$logs"
mkdir -p "$logs"
# The containers and ports the acts need must be free, or the rehearsal would test somebody else's.
for port in "$port_solo" "$port_a" "$port_b"; do
  ! demo_port_in_use "$port" || fail "port $port is in use; set DEMO_PORT_SOLO, DEMO_PORT_A and DEMO_PORT_B"
done
if [[ " $acts " == *" 2 "* ]]; then
  docker info >/dev/null 2>&1 || fail "the Docker daemon does not answer (docker info); start Docker"
  docker image inspect postgres:16-alpine >/dev/null 2>&1 || fail "the image postgres:16-alpine is not cached, and nothing is pulled: docker pull postgres:16-alpine while online"
fi

# ---------------------------------------------------------------------------------------------------------------------- setup
# Everything of the runbook's setup, for both acts, before either is shown: the hosts are started one after the other.

if [[ " $acts " == *" 1 "* ]]; then
  step "Setup: host solo (Sqlite): publish 1.0.0, restore, apply"
  run bash tools/demo/publish.sh 1 --host solo
  expect_has "the publish line" "$out" "published Elsa.Samples.Nuplane.Notes.1.0.0.nupkg to artifacts/demo/hosts/solo/feed"
  run bash tools/demo/run-host.sh solo --port "$port_solo" --management-key-env DEMO_KEY --prepare-only
  run bash tools/demo/elsa.sh persistence apply --restore --host "$solo_dir" "${sqlite_target[@]}"
  expect_match "the restore installed the packages" "$out" '^restore: [0-9]+ package\(s\) installed under '
  expect_match "the initial migration is applied" "$out" '^01 +Samples\.Notes +NotesSqliteDbContext +.*[ ]1 *$'
  step "Setup: host solo started"
  start_host solo "$port_solo"
  wait_ready solo "$port_solo"
fi

if [[ " $acts " == *" 2 "* ]]; then
  step "Setup: PostgreSQL container"
  run docker run -d --name "$demo_pg_container" -e POSTGRES_PASSWORD=demo -e POSTGRES_DB=elsa -p 127.0.0.1::5432 postgres:16-alpine
  demo_pg_connection="$(pg_connection)"
  pg_up() { docker exec "$demo_pg_container" pg_isready -q -h 127.0.0.1 -U postgres -d elsa; }
  wait_until "PostgreSQL accepts connections" 90 pg_up

  step "Setup: hosts a and b (PostgreSQL): publish 1.0.0, restore, apply"
  run bash tools/demo/publish.sh 1 --host a
  run bash tools/demo/publish.sh 1 --host b
  run with_pg bash tools/demo/run-host.sh a --port "$port_a" --provider PostgreSql --cluster host-a --fast-membership --management-key-env DEMO_KEY --prepare-only
  run with_pg bash tools/demo/elsa.sh persistence apply --restore --host "$a_dir" "${pg_target[@]}"
  expect_match "Cluster.Membership is applied" "$out" '^01 +Cluster\.Membership +.*[ ]1 *$'
  expect_match "Samples.Notes is applied" "$out" '^02 +Samples\.Notes +.*[ ]1 *$'
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
  stage note "$port_solo" "hello from release 1.0.0"
  expect_match "the note comes back with its id, text and time" "$out" '^\{"id":"[^"]+","text":"hello from release 1.0.0","createdAt":"[^"]+"\}$'
  stage note "$port_solo" "a second note"
  stage notes "$port_solo"
  expect_eq "two notes are listed" 2 "$(line_count "$out")"
  stage withtags "$port_solo"
  expect_eq "with-tags does not exist in 1.0.0, and nothing follows the status" "HTTP 404" "$out"
  stage rows solo
  expect_match "rows lists the columns" "$out" '^note +schema +tags$'
  expect_eq "both notes are stamped 1.0.0, before the tags column exists" 2 "$(notes_in "$out" '1\.0\.0' '\(no column yet\)')"

  step "Act 1.2 show the change"
  stage bash tools/demo/show-change.sh
  expect_has "the first heading" "$out" "==== The V2 folder: this is the whole change ===="
  full_change="$(bash tools/demo/show-change.sh)"
  expect_has "the tags endpoint is in the change" "$full_change" "NoteStore.Tags.cs"
  expect_has "the migration is in the change" "$full_change" "AddTags"
  expect_has "the last heading" "$full_change" "==== The package version ===="
  expect_has "the package version is shown" "$full_change" "<Version Condition=\"'\$(DemoVersion)' == '2'\">1.1.0</Version>"

  if [[ "$fallback" -eq 0 ]]; then
    step "Act 1.3-4 publish 1.1.0; the host installs it; /reload is refused (409)"
    publish_and_wait_for_refusal 2 solo
    stage reload "$port_solo"
    expect_eq "reload is refused" "HTTP 409" "$(first_line "$out")"
    expect_has "the refusal names the module" "$out" '"module": "Samples.Notes"'
    expect_has "the refusal has its code" "$out" '"code": "pending-migrations"'
    expect_match "the refusal names the migration" "$out" '^ +"[0-9]+_AddTags"$'
    expect_has "the apply command is relative to the repository" "$out" '"command": "dotnet elsa persistence apply --host \"artifacts/demo/hosts/solo\" --modules Samples.Notes --provider Sqlite --connection-env ELSA_EF_CONNECTION"'
    stage notes "$port_solo"
    expect_eq "the host keeps serving 1.0.0 meanwhile" 2 "$(line_count "$out")"

    step "Act 1.5 dotnet elsa persistence apply"
    apply_solo
    expect_has "the provider line" "$out" "provider: Sqlite   schema: (none)"
    expect_match "the apply table" "$out" '^#   MODULE +CONTEXT +HISTORY TABLE +APPLIED$'
    expect_match "the module's row" "$out" '^01 +Samples\.Notes +NotesSqliteDbContext +__EFMigrationsHistory_ElsaSamplesNotes +1 *$'

    step "Act 1.6 /reload answers 200; with-tags works; old notes show tags []"
    stage reload "$port_solo"
    expect_eq "reload succeeds" "HTTP 200" "$(first_line "$out")"
    expect_has "four features" "$out" '"features": 4'
    expect_has "one shell reloaded" "$out" '"reloaded": 1'
    expect_eq "the body is four lines of JSON" 5 "$(line_count "$out")"
    reloaded_at="$(now)"
  else
    step "Act 1.3-4 (fallback) stop the host, publish 1.1.0, start it: refused at start"
    kill -TERM "$(demo_host_pid solo)"
    gone() { ! kill -0 "$(<"$logs/solo.job")" 2>/dev/null; }
    wait_until "host solo stopped" 60 gone
    stage bash tools/demo/publish.sh 2 --host solo
    expect_has "the publish line" "$out" "published Elsa.Samples.Nuplane.Notes.1.1.0.nupkg to artifacts/demo/hosts/solo/feed"
    start_host solo "$port_solo"
    listening() { [[ "$(http_code "http://127.0.0.1:$port_solo/health/ready")" != "000" ]]; }
    wait_until "host solo answers again" 300 listening
    expect_eq "the shell is refused" 500 "$(http_code "http://127.0.0.1:$port_solo/demo/notes")"
    grep -q "has pending migrations" "$logs/solo.log" || fail "the host log does not say why the shell was refused"
    ok "the host log says the migrations are pending"
    expect_has "readiness says the module refused" "$(curl -s "http://127.0.0.1:$port_solo/health/ready")" "activation-refused"

    step "Act 1.5 (fallback) dotnet elsa persistence apply"
    apply_solo
    expect_match "the module's row" "$out" '^01 +Samples\.Notes +NotesSqliteDbContext +__EFMigrationsHistory_ElsaSamplesNotes +1 *$'

    # The host checks a refused shell again every minute on its own, so readiness may already be 200 here: no assertion on it.
    step "Act 1.6 (fallback) the next request activates the shell"
    stage notes "$port_solo"
    expect_eq "the shell is active and lists the notes" 2 "$(line_count "$out")"
    reloaded_at="$(now)"
  fi
  wait_until "with-tags answers 200 on solo" 60 with_tags_ok "$port_solo"
  measure "with-tags on solo answered 200 $(elapsed "$reloaded_at") s after the shell was re-composed"
  stage withtags "$port_solo"
  expect_eq "with-tags answers" "HTTP 200" "$(first_line "$out")"
  expect_eq "the status line and both old notes are printed, in lines" 3 "$(line_count "$out")"
  no_tags_on "$out"
  stage tag "$port_solo" demo
  expect_has "the tag is on the note" "$out" '"tags":["demo"]'
  stage rows solo
  expect_eq "two notes are stored" 2 "$(note_count "$out")"
  expect_eq "the tagged note was restamped 2.0.0" 1 "$(notes_in "$out" '2\.0\.0' '\["demo"\]')"
  stage notes "$port_solo"
  expect_eq "the base endpoint still works" 2 "$(line_count "$out")"
fi

# ------------------------------------------------------------------------------------------------------------------------ Act 2

if [[ " $acts " == *" 2 "* ]]; then
  step "Act 2.1 both hosts on 1.0.0"
  stage note "$port_a" "written on host A"
  stage note "$port_b" "written on host B"
  stage notes "$port_a"
  expect_has "a sees the note written on b" "$out" "written on host B"
  pg_status a
  expect_has "finalized at 1.0.0" "$out" "SamplesNotes (Samples.Notes): finalized at 1.0.0; this host reads [1.0.0]"
  expect_has "complete from 1.0.0" "$out" "  complete from 1.0.0"
  expect_match "two members" "$out" '^members: 2 in Cluster\.Membership, judged at .* with a skew allowance of 00:00:02$'
  expect_match "host-a is live" "$out" '^ +host-a: Active, live, last heartbeat '
  expect_match "host-b is live" "$out" '^ +host-b: Active, live, last heartbeat '
  expect_eq "both hosts read 1.0.0" 2 "$(printf '%s\n' "$out" | grep -c 'SamplesNotes: reads 1.0.0 *$' || true)"

  step "Act 2.2 upgrade b in place: publish, refused (409), apply, reload (200)"
  publish_and_wait_for_refusal 2 b
  stage reload "$port_b"
  expect_eq "reload of b is refused" "HTTP 409" "$(first_line "$out")"
  expect_has "the refusal names the module" "$out" '"module": "Samples.Notes"'
  expect_has "the refusal has its code" "$out" '"code": "pending-migrations"'
  expect_has "the apply command is relative to the repository" "$out" '"command": "dotnet elsa persistence apply --host \"artifacts/demo/hosts/b\"'
  stage with_pg bash tools/demo/elsa.sh persistence apply --host "$b_dir" "${pg_target[@]}"
  expect_match "Cluster.Membership has nothing to apply" "$out" '^01 +Cluster\.Membership +.*[ ]0 *$'
  expect_match "Samples.Notes applies AddTags" "$out" '^02 +Samples\.Notes +.*[ ]1 *$'
  stage reload "$port_b"
  expect_eq "reload of b succeeds" "HTTP 200" "$(first_line "$out")"
  expect_has "four features" "$out" '"features": 4'
  expect_has "one shell reloaded" "$out" '"reloaded": 1'

  step "Act 2.3 with-tags on b: 409 until every host can read 2.0.0"
  stage withtags "$port_b"
  expect_eq "with-tags on b is held back" "HTTP 409" "$(first_line "$out")"
  expect_has "the code" "$out" '"code":"schema-version-not-finalized"'
  expect_has "the feature" "$out" '"feature":"NotesWithTags"'
  expect_has "the reason" "$out" "every host can read version '2.0.0' of schema family 'SamplesNotes'"
  expect_has "nothing was saved" "$out" "The request was refused whole, and nothing it carried was saved."
  stage note "$port_a" "written by A on release 1.0.0"
  expect_has "a keeps writing" "$out" '"text":"written by A on release 1.0.0"'
  stage note "$port_b" "written by B on release 1.1.0"
  expect_has "b writes too" "$out" '"text":"written by B on release 1.1.0"'
  stage rows a
  expect_eq "four notes are stored" 4 "$(note_count "$out")"
  expect_eq "every note is still stamped 1.0.0, with nothing in its tags" 4 "$(notes_in "$out" '1\.0\.0' 'NULL')"
  expect_match "b runs 1.1.0 and still wrote the old schema version" "$out" '^written by B on release 1\.1\.0 +1\.0\.0 +NULL$'
  rows_on_a="$out"
  stage rows b
  expect_eq "a and b show one database" "$rows_on_a" "$out"

  step "Act 2.4 persistence status names host-a"
  pg_status b
  expect_has "finalized still at 1.0.0" "$out" "SamplesNotes (Samples.Notes): finalized at 1.0.0; this host reads [1.0.0, 2.0.0]"
  expect_has "2.0.0 is pending" "$out" "2.0.0: pending, held by nothing; waits for every counted member to read it"
  expect_has "2.0.0 waits for host-a" "$out" "waits for: host-a (reads 1.0.0)"
  expect_has "host-b reads both" "$out" "SamplesNotes: reads 1.0.0, 2.0.0"

  step "Act 2.5 upgrade a in place: no restart, no reload"
  stage bash tools/demo/publish.sh 2 --host a
  expect_has "the publish line" "$out" "published Elsa.Samples.Nuplane.Notes.1.1.0.nupkg to artifacts/demo/hosts/a/feed"
  published_at="$(now)"
  stage waitfor "$port_a"
  expect_match "host a switched: with-tags is no longer a 404" "$out" 'switched: with-tags answers HTTP (409|200) after '
  measure "host a answered with-tags with something but 404 $(elapsed "$published_at") s after the publish"
  # Host a has never loaded 1.1.0 before, so that line marks the install; the reload that follows it is the host switching over.
  a_reloaded() { sed -n '/Loaded package Elsa.Samples.Nuplane.Notes@1.1.0/,$p' "$logs/a.log" | grep -q "Reloaded 1 active shell"; }
  wait_until "host a installed the release and reloaded its own shell" 180 a_reloaded
  no_refusal="$(grep -c "was refused" "$logs/a.log" || true)"
  [[ "$no_refusal" -eq 0 ]] || fail "host a refused the reload $no_refusal time(s), though its database was already migrated"
  ok "host a was not refused (its database was migrated by b's apply)"

  step "Act 2.6 the version finalizes: both answer 200, status says finalized at 2.0.0"
  wait_until "with-tags answers 200 on b" 120 with_tags_ok "$port_b"
  wait_until "with-tags answers 200 on a" 120 with_tags_ok "$port_a"
  measure "2.0.0 finalized $(elapsed "$published_at") s after the publish to a (both hosts kept running)"
  # Not asserted, only reported: how far the host's backfill has come at the moment both hosts answer, which is a race by design.
  snapshot="$(rows a)"
  measure "rows at the moment both hosts answer 200: $(notes_in "$snapshot" '2\.0\.0' '\[\]') of 4 notes at 2.0.0 already, $(notes_in "$snapshot" '1\.0\.0' 'NULL') still at 1.0.0"
  stage withtags "$port_a"
  expect_eq "with-tags on a" "HTTP 200" "$(first_line "$out")"
  no_tags_on "$out"
  stage withtags "$port_b"
  expect_eq "with-tags on b" "HTTP 200" "$(first_line "$out")"
  no_tags_on "$out"
  pg_status a
  expect_has "finalized at 2.0.0" "$out" "SamplesNotes (Samples.Notes): finalized at 2.0.0; this host reads [1.0.0, 2.0.0]"
  expect_eq "both hosts read 2.0.0" 2 "$(printf '%s\n' "$out" | grep -c 'SamplesNotes: reads 1.0.0, 2.0.0 *$' || true)"
  stage note "$port_b" "written after finalization on B"
  stage note "$port_a" "written after finalization on A"
  stage rows a
  expect_eq "six notes are stored" 6 "$(note_count "$out")"
  expect_match "a note written on b is stamped 2.0.0, with an empty tag list" "$out" '^written after finalization on B +2\.0\.0 +\[\]$'
  expect_match "a note written on a is stamped 2.0.0, with an empty tag list" "$out" '^written after finalization on A +2\.0\.0 +\[\]$'
  measure "rows a few seconds after finalization: $(notes_in "$out" '2\.0\.0' '\[\]') of 6 notes at 2.0.0, $(notes_in "$out" '1\.0\.0' 'NULL') still at 1.0.0"
  all_rewritten() { [[ "$(notes_in "$(rows a)" '2\.0\.0' '\[\]')" -eq 6 ]]; }
  wait_until "the four old rows are rewritten to 2.0.0" 120 all_rewritten
  measure "the old rows were all at 2.0.0 $(elapsed "$published_at") s after the publish to a"
  backfill_complete() { grep -q "is complete at 2.0.0" "$logs/a.log" "$logs/b.log"; }
  wait_until "the backfill logged completion" 120 backfill_complete
  measure "the backfill completed $(elapsed "$published_at") s after the publish to a"
  pg_status a
  expect_has "complete from 2.0.0" "$out" "complete from 2.0.0"
  expect_match "host-a is live" "$out" '^ +host-a: Active, live'
  expect_match "host-b is live" "$out" '^ +host-b: Active, live'

  step "Act 2.7 the backfill: every row is 2.0.0"
  stage rows a
  expect_eq "six notes are stored" 6 "$(note_count "$out")"
  expect_eq "every note is stamped 2.0.0, with a tag list" 6 "$(notes_in "$out" '2\.0\.0' '\[\]')"
  expect_eq "no note is left at 1.0.0" 0 "$(notes_in "$out" '1\.0\.0' '.+')"
fi

step "Screen hygiene"
expect_eq "nothing the audience sees shows the repository path or a home folder" 0 "$(grep -cF -e "$demo_root" -e "$HOME" "$logs/stage.txt" || true)"
end_step

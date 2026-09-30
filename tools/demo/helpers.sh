# shellcheck shell=bash
# The presenter's helpers: short commands for the demo, sourced (never run) from the repository root by an interactive
# zsh or bash tab (source tools/demo/helpers.sh) and by tools/demo/rehearse.sh, so the rehearsal runs exactly what the presenter
# types. Nothing here may need bash-only syntax: it is read by zsh on stage. Paths are relative to the repository root.
#
#   note PORT TEXT      add a note                       notes PORT     list the notes
#   withtags PORT       GET /demo/notes/with-tags        tag PORT TAG   tag the first note
#   reload PORT         POST /_module-management/reload  status HOST    persistence status (solo, a or b)
#   waitfor PORT [S]    wait for host PORT to switch     pgconn         point this tab at the demo PostgreSQL

for _demo_tool in curl jq; do
  command -v "$_demo_tool" >/dev/null 2>&1 || { echo "error: $_demo_tool is not installed (brew install $_demo_tool)" >&2; return 1; }
done
unset _demo_tool

export DEMO_KEY="${DEMO_KEY:-demo-key}"

note() {
  curl -sS -X POST "localhost:$1/demo/notes" -H 'content-type: application/json' -d "$(jq -nc --arg text "$2" '{text: $text}')" | jq -c .
}

notes() {
  curl -sS "localhost:$1/demo/notes" | jq -c '.[]'
}

# The status line first, then the body, one JSON value per line.
withtags() {
  local body
  body="$(mktemp)"
  curl -sS -o "$body" -w 'HTTP %{http_code}\n' "localhost:$1/demo/notes/with-tags"
  jq -c 'if type == "array" then .[] else . end' "$body"
  rm -f "$body"
}

tag() {
  local id
  id="$(curl -sS "localhost:$1/demo/notes" | jq -r '.[0].id')"
  curl -sS -X POST "localhost:$1/demo/notes/$id/tags" -H 'content-type: application/json' -d "$(jq -nc --arg tag "$2" '{tags: [$tag]}')" | jq -c .
}

# The 409 names the command to run with an absolute path; on screen it is shown relative to the repository, so no user name is.
reload() {
  local body
  body="$(mktemp)"
  curl -sS -o "$body" -w 'HTTP %{http_code}\n' -X POST "localhost:$1/_module-management/reload" -H "X-Elsa-Module-Management-Key: $DEMO_KEY"
  jq --arg root "$(pwd -P)" '(if .shells then .shells[0] | {module, code, pendingMigrations, command} else . end)
    | if (.command | type) == "string" then .command |= (split($root + "/") | join("")) else . end' "$body"
  rm -f "$body"
}

# HOST is solo (Sqlite, Act 1) or a or b (PostgreSQL, Act 2: the tab needs pgconn, and the hosts run with --fast-membership, hence
# the 2 s skew allowance the members are judged with).
status() {
  case "$1" in
    solo) bash tools/demo/elsa.sh persistence status --host artifacts/demo/hosts/solo --environment Development --provider Sqlite --modules Samples.Notes --family SamplesNotes ;;
    *) bash tools/demo/elsa.sh persistence status --host "artifacts/demo/hosts/$1" --environment Development --provider PostgreSql --modules Samples.Notes,Cluster.Membership --skew-allowance 00:00:02 --family SamplesNotes ;;
  esac
}

# waitfor PORT [SECONDS]: the host on PORT has switched to release 1.1.0 once with-tags answers anything but 404 (409 while it
# evaluates the schema version, then 200). 000, the host not answering, is waited out too. Gives up after SECONDS (default 180).
waitfor() {
  local limit="${2:-180}" started="$SECONDS" code
  printf 'waiting for the host on port %s to switch' "$1"
  while :; do
    code="$(curl -s -o /dev/null -w '%{http_code}' -m 5 "localhost:$1/demo/notes/with-tags" || true)"
    [ "$code" != 404 ] && [ "$code" != 000 ] && break
    [ $((SECONDS - started)) -lt "$limit" ] || { echo " gave up after $limit s (HTTP $code)"; return 1; }
    printf .
    sleep 0.5
  done
  echo " switched: with-tags answers HTTP $code after $((SECONDS - started)) s"
}

# The PostgreSQL connection of the demo container, whose port Docker picked. It is exported for this tab only, and never printed.
pg_connection() {
  local port
  port="$(docker port "${DEMO_PG_CONTAINER:-elsa-demo-pg}" 5432/tcp | head -1 | sed 's/.*://')"
  [ -n "$port" ] || { echo "error: the PostgreSQL container ${DEMO_PG_CONTAINER:-elsa-demo-pg} does not run or publishes no port" >&2; return 1; }
  echo "Host=127.0.0.1;Port=$port;Database=elsa;Username=postgres;Password=demo"
}

pgconn() {
  local connection
  connection="$(pg_connection)" || return 1
  export ELSA_EF_CONNECTION="$connection"
}

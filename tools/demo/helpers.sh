# shellcheck shell=bash
# The presenter's helpers: short commands for the demo, sourced (never run) from the repository root by an interactive
# zsh or bash tab (source tools/demo/helpers.sh) and by tools/demo/rehearse.sh, so the rehearsal runs exactly what the presenter
# types. Nothing here may need bash-only syntax: it is read by zsh on stage. Paths are relative to the repository root.
#
#   note PORT TEXT      add a note                       notes PORT     list the notes
#   withtags PORT       GET /demo/notes/with-tags        tag PORT TAG   tag the first note
#   reload PORT         POST /_module-management/reload  status HOST    persistence status (solo, a or b)
#   waitfor PORT [S]    wait for host PORT to switch     pgconn         point this tab at the demo PostgreSQL
#   rows HOST           the Notes table: each row's schema version and tags, read from the database itself (solo, a or b)

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

# HOST is solo (the Sqlite file) or a or b (the PostgreSQL container, through docker exec and psql: a and b share one database, so
# both show the same rows). One line per note, oldest first: the schema version stamped on the row when it was written, and its tags column as
# stored (NULL: written by release 1.0.0, which has no such column to fill; [] or a list: written by 2.0.0). It reads the tables
# themselves, so it shows what is stored and not what a host answers. A database the AddTags migration has not reached yet has no
# tags column, and says so. It only reads: the Sqlite file is opened with query_only and a 2 s busy timeout, the PostgreSQL session is
# read-only. (The Sqlite file is in WAL mode, which a read-only open cannot serve without its -shm file: the query only selects.)
# The columns are separated by a control character, so a note that contains a | keeps its columns. A probe that fails (no container, no
# database, no file) is reported once, and rows returns 1, instead of pretending the column is missing.
_rows_pg() {
  docker exec -e PGOPTIONS=-cdefault_transaction_read_only=on "${DEMO_PG_CONTAINER:-elsa-demo-pg}" psql -U postgres -d elsa -X "$@" 2>&1
}

rows() {
  local table=elsa_samples_notes container="${DEMO_PG_CONTAINER:-elsa-demo-pg}" file=artifacts/demo/notes.db sep=$'\x1f' probe rc tags query out
  command -v column >/dev/null 2>&1 || { echo "error: column is not installed" >&2; return 1; }
  case "$1" in
    solo)
      command -v sqlite3 >/dev/null 2>&1 || { echo "error: sqlite3 is not installed (brew install sqlite)" >&2; return 1; }
      [ -f "$file" ] || { echo "error: there is no Sqlite database at $file yet" >&2; return 1; }
      probe="$(sqlite3 -cmd '.timeout 2000' -cmd 'pragma query_only=on' "$file" "select count(*) from pragma_table_info('$table') where name = 'TagsJson'" 2>&1)"; rc=$?
      [ "$rc" -eq 0 ] || { echo "error: the Sqlite database $file could not be read: $probe" >&2; return 1; }
      tags="'(no column yet)'"
      [ "$probe" = 1 ] && tags="coalesce(TagsJson, 'NULL')"
      query="select Text as note, SchemaVersion as schema, $tags as tags from $table order by CreatedAt"
      out="$(sqlite3 -cmd '.timeout 2000' -cmd 'pragma query_only=on' -header -separator "$sep" "$file" "$query" 2>&1)"; rc=$?
      [ "$rc" -eq 0 ] || { echo "error: the Sqlite database $file could not be read: $out" >&2; return 1; }
      printf '%s\n' "$out" | column -t -s "$sep"
      ;;
    a|b)
      command -v docker >/dev/null 2>&1 || { echo "error: docker is not installed" >&2; return 1; }
      probe="$(_rows_pg -At -c "select count(*) from information_schema.columns where table_name = '$table' and column_name = 'TagsJson'")"; rc=$?
      if [ "$rc" -ne 0 ]; then
        case "$probe" in
          *"No such container"*|*"is not running"*) echo "error: the PostgreSQL container $container is not running" >&2 ;;
          *'database "elsa" does not exist'*) echo "error: the database elsa does not exist in the PostgreSQL container $container" >&2 ;;
          *) echo "error: the PostgreSQL container $container could not be read: $(printf '%s' "$probe" | head -1)" >&2 ;;
        esac
        return 1
      fi
      tags="'(no column yet)'"
      [ "$probe" = 1 ] && tags="coalesce(\"TagsJson\", 'NULL')"
      query="select \"Text\" as note, \"SchemaVersion\" as schema, $tags as tags from $table order by \"CreatedAt\""
      out="$(_rows_pg -q -A -F "$sep" -P footer=off -c "$query")"; rc=$?
      [ "$rc" -eq 0 ] || { echo "error: the PostgreSQL container $container could not be read: $(printf '%s' "$out" | head -1)" >&2; return 1; }
      printf '%s\n' "$out" | column -t -s "$sep"
      ;;
    *) echo "usage: rows solo|a|b" >&2; return 1 ;;
  esac
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

#!/usr/bin/env bash
# Puts the demo back to a clean start between rehearsals: stops the demo hosts, removes the PostgreSQL container and the demo's
# state, and prints what it removed. Everything it stops or removes is named in the output.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/reset.sh [--all]

Stops every demo host recorded in artifacts/demo/pids, the Workbench and Studio of Act 3 included (by process id, and only a
process that is still a demo host; nothing is ever stopped by name), removes the PostgreSQL container (elsa-demo-pg, or
DEMO_PG_CONTAINER) and everything under artifacts/demo except the staged releases and the closure feed, which prepack.sh spent
minutes making.
  --all   remove the staged releases and the closure feed as well; run tools/demo/prepack.sh again afterwards

It exits 1 when Docker could not be asked about the container (a daemon that is not running): everything else is still cleaned.
USAGE
}

remove_all=0
case "${1:-}" in
  "") ;;
  --all) remove_all=1 ;;
  -h|--help) usage; exit 0 ;;
  *) demo_fail "unknown argument '$1' (see --help)." ;;
esac
[[ $# -le 1 ]] || demo_fail "unknown argument '$2' (see --help)."

# Hosts first, so nothing holds a database or a directory open while it is removed. SIGTERM lets a host leave the cluster
# cleanly; only a host that has not stopped after 30 s is killed.
if [[ -d "$demo_pids" ]]; then
  for file in "$demo_pids"/*.pid; do
    [[ -e "$file" ]] || continue
    name="$(basename "$file" .pid)"
    pid="$(demo_host_pid "$name")"
    if [[ -z "$pid" ]]; then
      echo "host $name: not running"
      continue
    fi
    kill -TERM "$pid"
    for _ in $(seq 1 60); do
      kill -0 "$pid" 2>/dev/null || break
      sleep 0.5
    done
    if kill -0 "$pid" 2>/dev/null; then
      kill -KILL "$pid"
      echo "host $name: process $pid did not stop in 30 s and was killed"
    else
      echo "host $name: process $pid stopped"
    fi
  done
fi

docker_failed=0
if command -v docker >/dev/null 2>&1; then
  # docker's own status is tested apart from what it printed: a daemon that is down prints nothing on stdout, and that must
  # not read as "no container".
  listed_status=0
  listed="$(docker ps -a --filter "name=^/$demo_pg_container\$" --format '{{.ID}}' 2>&1)" || listed_status=$?
  if [[ "$listed_status" -ne 0 ]]; then
    echo "container $demo_pg_container: docker ps failed (exit $listed_status), so the container was not looked for: ${listed:-no output}" >&2
    docker_failed=1
  elif [[ -n "$listed" ]]; then
    docker rm -f "$demo_pg_container" >/dev/null
    echo "container $demo_pg_container: removed"
  else
    echo "container $demo_pg_container: none"
  fi
else
  echo "container $demo_pg_container: docker is not installed, nothing to remove"
fi

if [[ -d "$demo_artifacts" ]]; then
  for path in "$demo_artifacts"/* "$demo_artifacts"/.[!.]*; do
    [[ -e "$path" ]] || continue
    if [[ "$remove_all" -eq 0 && ( "$path" == "$demo_staging" || "$path" == "$demo_closure" ) ]]; then
      echo "kept ${path#"$demo_root"/} (--all removes it)"
      continue
    fi
    rm -rf "$path"
    echo "removed ${path#"$demo_root"/}"
  done
fi
if [[ "$docker_failed" -eq 1 ]]; then
  echo "demo state is clean, except that Docker did not answer: start Docker, then run bash tools/demo/reset.sh again" >&2
  exit 1
fi
echo "demo state is clean"

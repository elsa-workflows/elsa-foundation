#!/usr/bin/env bash
# Puts the demo back to a clean start between rehearsals: stops the demo hosts, removes the PostgreSQL container and the demo's
# state, and prints what it removed. Everything it stops or removes is named in the output.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat >&2 <<'USAGE'
Usage: bash tools/demo/reset.sh [--all]

Stops every demo host recorded in artifacts/demo/pids (by process id, and only a process that is still a demo host; nothing is
ever stopped by name), removes the PostgreSQL container (elsa-demo-pg, or DEMO_PG_CONTAINER) and everything under artifacts/demo
except the staged releases and the closure feed, which prepack.sh spent minutes making.
  --all   remove the staged releases and the closure feed as well; run tools/demo/prepack.sh again afterwards
USAGE
  exit 2
}

remove_all=0
case "${1:-}" in
  "") ;;
  --all) remove_all=1 ;;
  *) usage ;;
esac

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

if command -v docker >/dev/null 2>&1; then
  if [[ -n "$(docker ps -a --filter "name=^/$demo_pg_container\$" --format '{{.ID}}')" ]]; then
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
echo "demo state is clean"

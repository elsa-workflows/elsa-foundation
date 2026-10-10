#!/usr/bin/env bash
# Stops the Workbench and Studio process groups that setup.sh started, and nothing else.
set -euo pipefail

state="$(cd "$(dirname "$0")" && pwd)/.state"
[ -f "$state/env" ] || { echo "Nothing to stop: no $state/env."; exit 0; }
# shellcheck disable=SC1091
. "$state/env"

for pid in "${STUDIO_PID:-}" "${WORKBENCH_PID:-}"; do
  [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null || continue
  kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid"
  for _ in $(seq 20); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done
  kill -0 "$pid" 2>/dev/null && { kill -KILL -- "-$pid" 2>/dev/null || kill -KILL "$pid"; }
  echo "Stopped process group $pid."
done
rm -f "$state/env"

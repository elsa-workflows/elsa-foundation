#!/usr/bin/env bash
# Stops the Workbench and Studio process groups that setup.sh started, and nothing else. Reads .state/env as data.
set -euo pipefail

state="$(cd "$(dirname "$0")" && pwd)/.state"
[ -f "$state/env" ] || { echo "Nothing to stop: no $state/env."; exit 0; }
state_get() { sed -n "s/^$1=//p" "$state/env" | tail -1; }

for pid in "$(state_get STUDIO_PID)" "$(state_get WORKBENCH_PID)"; do
  [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null || continue
  kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid"
  for _ in $(seq 20); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done
  kill -0 "$pid" 2>/dev/null && { kill -KILL -- "-$pid" 2>/dev/null || kill -KILL "$pid"; }
  echo "Stopped process group $pid."
done
rm -f "$state/env"

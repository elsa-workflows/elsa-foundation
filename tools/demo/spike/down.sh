#!/usr/bin/env bash
# Spike only: stops the processes tools/demo/spike/up.sh started, by the process ids they recorded, and only when that
# process is still the spike's Workbench or Studio (a recorded id may since belong to something else).
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
for file in "$root"/artifacts/spike/pids/*.pid; do
  [[ -f "$file" ]] || continue
  pid="$(<"$file")"
  command="$(ps -ww -p "$pid" -o command= 2>/dev/null || true)"
  if [[ "$command" == *"Elsa.Workbench.dll"*"$root/artifacts/spike/hosts/"* || "$command" == *"Elsa.Studio.Web.dll"* ]]; then
    kill "$pid" && echo "stopped $(basename "$file" .pid) ($pid)"
    for _ in $(seq 1 30); do ps -p "$pid" >/dev/null 2>&1 || break; sleep 1; done
  fi
  rm -f "$file"
done

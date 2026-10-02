#!/usr/bin/env bash
# Spike only: brings the designer spike up from a clean state and leaves it running.
#
#   bash tools/demo/spike/stage.sh            (once, and after any code change; about a minute when built)
#   bash tools/demo/spike/up.sh [--validate]  (about a minute)
#   bash tools/demo/spike/down.sh             (stops both processes)
#
# 1. Resets the Workbench spike host (artifacts/spike/hosts/wb: its Sqlite files, its Nuplane state, its feed).
# 2. Publishes release 1 (Notes 1.0.0 + Add note activity 1.0.0) into its feed.
# 3. Starts the Workbench on http://localhost:7211 (log artifacts/spike/logs/wb.log) and waits until its shell is ready.
#    With --validate it starts it once under AutoMigrate, which creates every table of the fresh database, stops it, and
#    starts it again under Validate, the demo's policy: the upgrade's migration is then refused until it is applied
#    (tools/demo/spike/apply.sh), as in Act 1. Without it the shell applies the migration itself when it reloads.
# 4. Starts Studio on http://localhost:7221 against it (log artifacts/spike/logs/studio-7221.log). Studio must be built:
#    see run-studio.sh.
#
# Then, without restarting anything:
#   bash tools/demo/spike/publish.sh 2       release 1.1.0 into the feed; the host installs it (~12 s) and refreshes its
#                                            feature catalog ("Refreshed runtime feature catalog" in wb.log)
#   bash tools/demo/spike/reload.sh          rebuilds the shell from it; under --validate the first reload is refused with
#                                            the pending migration, then: bash tools/demo/spike/apply.sh, and reload again
#   refresh the Studio page                  Add note 1.1.0, with Tags, is in the catalog beside 1.0.0
#   bash tools/demo/spike/scenario.sh        does all of that through the APIs and runs both versions
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
cd "$root"
validate=0
[[ "${1:-}" != "--validate" ]] || validate=1
logs="$root/artifacts/spike/logs"
mkdir -p "$logs"
for release in 1 2; do
  compgen -G "artifacts/spike/staging/notes-$release/*.nupkg" >/dev/null || { echo "not staged: bash tools/demo/spike/stage.sh" >&2; exit 1; }
done
bash tools/demo/spike/down.sh >/dev/null 2>&1 || true

rm -rf artifacts/spike/hosts/wb
bash tools/demo/spike/run-workbench.sh --prepare-only
bash tools/demo/spike/publish.sh 1

start_workbench() {
  SPIKE_MIGRATE_POLICY="$1" nohup bash tools/demo/spike/run-workbench.sh >"$logs/wb.log" 2>&1 &
  echo -n "waiting for the Workbench ($1)"
  until grep -q -E "is ready after" "$logs/wb.log" 2>/dev/null; do
    grep -q -E "Unhandled exception|Application startup exception" "$logs/wb.log" 2>/dev/null && { echo; echo "the Workbench failed to start; see $logs/wb.log" >&2; exit 1; }
    echo -n "."; sleep 2
  done
  echo " ready"
}

start_workbench AutoMigrate
if [[ "$validate" -eq 1 ]]; then
  bash tools/demo/spike/down.sh >/dev/null
  start_workbench Validate
fi

nohup bash tools/demo/spike/run-studio.sh >"$logs/studio-7221.log" 2>&1 &
echo -n "waiting for Studio"
until curl -s -o /dev/null http://localhost:7221/studio-runtime.js; do echo -n "."; sleep 1; done
echo " ready"

cat <<EOF

Studio:      http://localhost:7221   (sign in with the Workbench's development admin: user and password are
             SeedAdminUserName/SeedAdminPassword in artifacts/spike/hosts/wb/shells.json)
Backend:     http://localhost:7211   (Workbench; log artifacts/spike/logs/wb.log)
Activity:    "Add note" in the Notes category of the designer's palette, release 1.0.0 (input: Text)
Upgrade:     bash tools/demo/spike/publish.sh 2, wait for "Refreshed runtime feature catalog" in the log,
             bash tools/demo/spike/reload.sh$( [[ "$validate" -eq 1 ]] && echo " (refused: pending migration), bash tools/demo/spike/apply.sh, reload.sh again")
             then refresh the Studio page
Rows:        python3 tools/demo/spike/rows.py
Stop:        bash tools/demo/spike/down.sh
EOF

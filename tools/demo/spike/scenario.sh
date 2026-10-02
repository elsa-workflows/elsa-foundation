#!/usr/bin/env bash
# Spike only: the designer scenario through the APIs Studio uses, on a spike that tools/demo/spike/up.sh just brought up
# (release 1 in the feed). Nothing is restarted: the Workbench process keeps running throughout.
#
#   1. Add note 1.0.0 is in the catalog; a workflow with it runs and writes a row stamped 1.0.0.
#   2. Release 1.1.0 is published into the feed; the host installs it and refreshes its feature catalog.
#   3. The shell is reloaded: the migration is applied (by the shell under AutoMigrate; under Validate, from up.sh
#      --validate, the reload is refused and apply.sh applies it), 2.0.0 finalizes, and Add note 1.1.0, with Tags,
#      is in the catalog beside 1.0.0.
#   3b. Tries to recommend Add note 1.1.0, the version the palette offers for new nodes (placed nodes keep theirs). Today
#      this is refused (409) for a source-owned activity, whose definition has no tenant, by a user of tenant 'default'.
#   4. A workflow with Add note 1.1.0 runs and writes a row stamped 2.0.0 with its tags.
#   5. The workflow published in step 1 (Add note 1.0.0) runs again, now on the 1.1.0 code.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
cd "$root"
spike=tools/demo/spike
log=artifacts/spike/logs/wb.log
step() { echo; echo "== $*"; }

bash $spike/token.sh > artifacts/spike/hosts/wb/.token

step "1. release 1.0.0: the catalog, a run, the rows"
python3 $spike/catalog.py "Notes.Activities.AddNote" | grep -E '"version"|"name"'
python3 $spike/run-activity.py AddNote 1.0.0 "Text=written by Add note 1.0.0" --name "Add note 1.0.0" --save artifacts/spike/v1-workflow.json
python3 $spike/rows.py

step "2. publish release 1.1.0 into the feed"
python3 $spike/logmark.py mark $log
started=$SECONDS
bash $spike/publish.sh 2
python3 $spike/logmark.py wait $log "Refreshed runtime feature catalog" 120
echo "installed and catalog refreshed after $((SECONDS - started)) s"

step "3. reload the shell"
reloaded="$(bash $spike/reload.sh)"
echo "$reloaded"
if [[ "$reloaded" == *EfPendingMigrationsException* ]]; then
  step "3a. refused under Validate: apply the pending migration out of process, then reload again"
  bash $spike/apply.sh
  bash $spike/reload.sh
fi
python3 $spike/logmark.py show $log "finalized|fail:" | head -5
python3 $spike/catalog.py "Notes.Activities.AddNote" | grep -E '"version"|"name"'

step "3b. recommend Add note 1.1.0, the version the designer's palette offers for a new node"
bash $spike/token.sh > artifacts/spike/hosts/wb/.token
python3 $spike/recommend.py AddNote 1.1.0 || echo "(recommendation refused: see the spike report; the designer offers Change exact version instead)"

step "4. Add note 1.1.0 with tags"
python3 $spike/run-activity.py AddNote 1.1.0 "Text=written by Add note 1.1.0" "Tags=demo, designer" --name "Add note 1.1.0"

step "5. the workflow published with Add note 1.0.0, run again"
python3 $spike/run-activity.py --rerun artifacts/spike/v1-workflow.json
python3 $spike/rows.py

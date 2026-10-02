#!/usr/bin/env bash
# Spike only: `dotnet elsa persistence` against the Workbench spike host's Notes database, out of process and inside that
# host's closure (tools/demo/elsa.sh). Without arguments it applies the Notes module's pending migrations, as the reload
# refusal says; otherwise the first argument is the persistence command and the rest are its own options.
#
#   bash tools/demo/spike/apply.sh                       apply
#   bash tools/demo/spike/apply.sh status
#   bash tools/demo/spike/apply.sh hold --family SamplesNotes --version 2.0.0 --reason "..." --operator demo
#   bash tools/demo/spike/apply.sh release --family SamplesNotes --version 2.0.0 --operator demo
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
command="${1:-apply}"
[[ $# -eq 0 ]] || shift
export ELSA_EF_CONNECTION="Data Source=$root/artifacts/spike/hosts/wb/notes.db;Pooling=False"
exec bash "$root/tools/demo/elsa.sh" persistence "$command" --host artifacts/spike/hosts/wb --environment Development \
  --provider Sqlite --modules Samples.Notes "$@"

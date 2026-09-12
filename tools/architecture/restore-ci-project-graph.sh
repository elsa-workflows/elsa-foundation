#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/../.." && pwd -P)"
cd "$repo_root"

# The architecture suite compares both configurations. Keep Debug's evaluated graph
# isolated so it cannot replace the ordinary Release assets consumed by --no-restore
# build and test steps.
dotnet restore Elsa.Server.slnx -p:Configuration=Release "$@"
dotnet restore Elsa.Server.slnx -p:Configuration=Debug -p:BaseIntermediateOutputPath=obj/ef-guard/Debug/ "$@"

#!/usr/bin/env bash
# Add a named migration for every Secrets EF derived context into the module assembly
# Nuplane loads at apply time. Do not re-add Initial; that set already exists.
#
# Usage:
#   bash tools/ef/generate-ef-migrations.sh <MigrationName>
#
# Requires the dotnet-ef tool (10.0.x). From the repo root: `dotnet tool restore`
# or `dotnet tool install dotnet-ef --version 10.0.10 --tool-path .tools`.
set -euo pipefail

# shellcheck source=secrets-ef-lib.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)/secrets-ef-lib.sh"
secrets_ef_init
cd "$secrets_ef_root"

if [[ $# -lt 1 || "$1" == "-h" || "$1" == "--help" ]]; then
  echo "Usage: bash tools/ef/generate-ef-migrations.sh <MigrationName>" >&2
  exit 2
fi

name="$1"
if [[ "$name" == "Initial" ]]; then
  echo "error: Initial already exists for each derived Secrets context. Choose a new name." >&2
  exit 2
fi

for row in "${secrets_ef_contexts[@]}"; do
  secrets_ef_split "$row"
  echo "migrations add $name --context $secrets_ef_context"
  secrets_ef migrations add "$name" \
    --context "$secrets_ef_context" \
    --project "$secrets_ef_module" \
    --startup-project "$secrets_ef_startup" \
    --output-dir "$secrets_ef_output_dir" \
    --namespace "$secrets_ef_namespace"
done

echo "Review provider snapshots before committing. If a snapshot emits provider-package"
echo "extension calls, rewrite them to Relational HasColumnType / annotations so the"
echo "module stays provider-free."

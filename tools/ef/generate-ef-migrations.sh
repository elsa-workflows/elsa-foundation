#!/usr/bin/env bash
# Add a named migration for every Secrets EF derived context into the module assembly
# Nuplane loads at apply time. Do not re-add Initial; that set already exists.
#
# Usage:
#   bash tools/ef/generate-ef-migrations.sh <MigrationName>
#
# Requires the dotnet-ef tool (10.0.x). From the repo root: `dotnet tool restore`
# or `dotnet tool install dotnet-ef --version 10.0.10 --tool-path .tools`.
#
# The startup project is the shared tools/ef/Elsa.EntityFrameworkCore.Tooling (#1878). Generating a
# migration never opens a connection, so its design-time factories bind a placeholder one.
set -euo pipefail

# shellcheck source=secrets-ef-lib.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)/secrets-ef-lib.sh"
secrets_ef_init
cd "$secrets_ef_root"

# A schema belongs to a deployment, not to a migration: the shared tooling project's factories honour
# ELSA_EF_SCHEMA, and scaffolding with one set would bake it into the model snapshot so no host could
# choose another. Cleared here for the same reason generate-module-migrations.sh clears it.
unset ELSA_EF_SCHEMA

if [[ $# -lt 1 || "$1" == "-h" || "$1" == "--help" ]]; then
  echo "Usage: bash tools/ef/generate-ef-migrations.sh <MigrationName>" >&2
  exit 2
fi

name="$1"
if [[ "$name" == "Initial" ]]; then
  echo "error: Initial already exists for each derived Secrets context. Choose a new name." >&2
  exit 2
fi

module_dir="$(dirname "$secrets_ef_module")"

for row in "${secrets_ef_contexts[@]}"; do
  secrets_ef_split "$row"
  echo "migrations add $name --context $secrets_ef_context"
  secrets_ef migrations add "$name" \
    --context "$secrets_ef_context" \
    --project "$secrets_ef_module" \
    --startup-project "$secrets_ef_startup" \
    --output-dir "$secrets_ef_output_dir" \
    --namespace "$secrets_ef_namespace"
  # The module stays provider-free, so the few provider-package calls EF emits into the snapshot are
  # rewritten before the next context is generated: the next `migrations add` builds this module, and
  # an un-rewritten snapshot from the previous one would fail that build.
  bash "$secrets_ef_root/tools/ef/strip-provider-calls.sh" "$module_dir/$secrets_ef_output_dir"
done

echo "Review the provider snapshots before committing."

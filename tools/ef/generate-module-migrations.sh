#!/usr/bin/env bash
# Regenerates the Initial migration of every first-party EF module context, per provider.
#
# Usage:
#   bash tools/ef/generate-module-migrations.sh [context-regex]
#   bash tools/ef/generate-module-migrations.sh --runtime-incremental MigrationName
#   bash tools/ef/generate-module-migrations.sh --design-incremental MigrationName
#
# Elsa is pre-release and ships no production data, so each module keeps one Initial migration
# per provider that is regenerated whenever its model changes. Secrets keeps its historical
# migration chain. Runtime and Workflows Design retain saved-execution and authoring history.
#
# Regeneration ends when 4.0 ships as a stable release (ADR 0078, issue #1976): each Initial is
# then frozen as its module's baseline and later model changes add incremental migrations. A
# regenerated Initial gets a new migration id, which breaks every database that applied the old one.
# Migrations compile into the module assembly, which stays provider-free: the post-processing
# step below rewrites the few provider-package calls EF emits.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"
mode="regenerate"
filter="${1:-.*}"
migration_name="Initial"
if [[ "$filter" == "--runtime-incremental" || "$filter" == "--design-incremental" ]]; then
  mode="incremental"
  selected_mode="$filter"
  migration_name="${2:-}"
  if [[ $# -ne 2 || ! "$migration_name" =~ ^[A-Za-z][A-Za-z0-9_]*$ || "$migration_name" == "Initial" ]]; then
    echo "Usage: $0 $selected_mode MigrationName (a new, non-Initial identifier)" >&2
    exit 2
  fi
  if [[ "$selected_mode" == "--runtime-incremental" ]]; then
    filter='Runtime(Sqlite|SqlServer|PostgreSql|MySql)DbContext'
  else
    filter='WorkflowsDesign(Sqlite|SqlServer|PostgreSql|MySql)DbContext'
  fi
elif [[ "$filter" == --* || $# -gt 1 ]]; then
  echo "Usage: $0 [context-regex] | --runtime-incremental MigrationName | --design-incremental MigrationName" >&2
  exit 2
fi
tooling="tools/ef/Elsa.EntityFrameworkCore.Tooling/Elsa.EntityFrameworkCore.Tooling.csproj"
configuration="${ELSA_EF_CONFIGURATION:-Release}"
module_roots=(Essentials/src)
for capability in Extensions/*/src; do
  [[ -d "$capability" ]] && module_roots+=("$capability")
done

# A schema belongs to a deployment, not to a migration: scaffolding with one bakes it into the model
# snapshot and no host could choose another. Applying and scripting still honour ELSA_EF_SCHEMA.
unset ELSA_EF_SCHEMA

dotnet tool restore >/dev/null

module_project() {
  local assembly="$1"
  # Cover backend modules without admitting colocated Studio or extension tests.
  find "${module_roots[@]}" -name "$assembly.csproj" -not -path '*/obj/*' | head -n 1
}

output_dir() {
  local context="$1" provider="$2" assembly="$3"
  local base="${context%"$provider"DbContext}"
  if [[ "$assembly" == "Elsa.Secrets.Persistence.EntityFrameworkCore" ]]; then
    echo "Migrations/$provider"
  else
    echo "Migrations/$base/$provider"
  fi
}

build_tooling() {
  local log
  log="$(mktemp)"
  if ! dotnet build "$tooling" -c "$configuration" -v q -nologo >"$log" 2>&1; then
    grep -E " error " "$log" | sort -u >&2 || cat "$log" >&2
    rm -f "$log"
    echo "Tooling build failed. A previous half-generated migration set may need deleting first." >&2
    exit 1
  fi
  rm -f "$log"
}

# Old migrations can stop the tooling from building, so find the matching sets by snapshot name
# and delete them before the first build. Secrets keeps its historical chain.
while IFS= read -r snapshot; do
  name="$(basename "$snapshot" ModelSnapshot.cs)"
  [[ "$name" =~ ^($filter)$ ]] || continue
  # #2898 retains real saved executions and their Design authoring. Preserve both histories in every mode.
  [[ "$mode" == "incremental" || "$name" =~ ^(Runtime|WorkflowsDesign)(Sqlite|SqlServer|PostgreSql|MySql)DbContext$ ]] && continue
  [[ "$snapshot" == */Secrets/* && "$name" != SecretsMySqlDbContext ]] && continue
  rm -rf "$(dirname "$snapshot")"
done < <(find "${module_roots[@]}" -name '*ModelSnapshot.cs' -path '*/EntityFrameworkCore/Migrations/*' -not -path '*/obj/*')

build_tooling
rows=()
while IFS= read -r line; do rows+=("$line"); done < <(dotnet run --project "$tooling" -c "$configuration" --no-build -- list | grep -E "^($filter)[|]" || true)
if [[ ${#rows[@]} -eq 0 ]]; then
  echo "No module context matches '$filter'." >&2
  exit 2
fi

# Remove every target first, then rebuild once, so no compiled snapshot of the old Initial
# leaks into the new diff.
targets=()
target_count=0
for row in "${rows[@]}"; do
  IFS='|' read -r context provider assembly namespace <<<"$row"
  project="$(module_project "$assembly")"
  dir="$(dirname "$project")/$(output_dir "$context" "$provider" "$assembly")"
  if [[ "$mode" != "incremental" && ( "$assembly" == "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore" || "$assembly" == "Elsa.Workflows.Design.Persistence.EntityFrameworkCore" ) ]]; then
    echo "skip $context: retained Runtime/Design history requires the corresponding incremental mode"
    continue
  fi
  if [[ "$assembly" == "Elsa.Secrets.Persistence.EntityFrameworkCore" && -d "$dir" ]]; then
    echo "skip $context: Secrets keeps its migration chain"
    continue
  fi
  if [[ "$mode" != "incremental" ]]; then rm -rf "$dir"; fi
  targets+=("$context|$provider|$project|$(output_dir "$context" "$provider" "$assembly")|$namespace")
  target_count=$((target_count + 1))
done

if [[ "$target_count" -eq 0 ]]; then
  echo "No migration sets require regeneration; retained histories were left unchanged."
  exit 0
fi

build_tooling

for target in "${targets[@]}"; do
  IFS='|' read -r context provider project dir namespace <<<"$target"
  echo "migrations add $migration_name --context $context"
  dotnet ef migrations add "$migration_name" \
    --context "$context" \
    --project "$project" \
    --startup-project "$tooling" \
    --output-dir "$dir" \
    --namespace "$namespace.${dir//\//.}" \
    --configuration "$configuration" \
    --no-build >/dev/null
  module_dir="$(dirname "$project")"
  # dotnet ef places a first snapshot by namespace rather than beside the migration; keep them together.
  snapshot="$(find "$module_dir" -name "${context}ModelSnapshot.cs" -not -path '*/obj/*' -not -path '*/bin/*' | head -n 1)"
  if [[ -n "$snapshot" && "$(dirname "$snapshot")" != "$module_dir/$dir" ]]; then
    stray_root="$module_dir/$(echo "${snapshot#"$module_dir"/}" | cut -d/ -f1)"
    mv "$snapshot" "$module_dir/$dir/"
    find "$stray_root" -type d -empty -delete 2>/dev/null || true
  fi
  bash "$root/tools/ef/strip-provider-calls.sh" "$module_dir/$dir"
done

echo "Updated ${#targets[@]} migration set(s) ($mode). Build the modules and run the EF migration tests before committing."

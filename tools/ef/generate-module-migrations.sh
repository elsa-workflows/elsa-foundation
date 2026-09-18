#!/usr/bin/env bash
# Regenerates the Initial migration of every first-party EF module context, per provider.
#
# Usage:
#   bash tools/ef/generate-module-migrations.sh [context-regex]
#
# Elsa is pre-release and ships no production data, so each module keeps one Initial migration
# per provider that is regenerated whenever its model changes. Secrets keeps its historical
# migration chain; only a provider with no Secrets migrations yet gets an Initial here.
# Migrations compile into the module assembly, which stays provider-free: the post-processing
# step below rewrites the few provider-package calls EF emits.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"
filter="${1:-.*}"
tooling="tools/ef/Elsa.EntityFrameworkCore.Tooling/Elsa.EntityFrameworkCore.Tooling.csproj"
configuration="${ELSA_EF_CONFIGURATION:-Release}"

# A schema belongs to a deployment, not to a migration: scaffolding with one bakes it into the model
# snapshot and no host could choose another. Applying and scripting still honour ELSA_EF_SCHEMA.
unset ELSA_EF_SCHEMA

dotnet tool restore >/dev/null

module_project() {
  local assembly="$1"
  find src -name "$assembly.csproj" -not -path '*/obj/*' | head -n 1
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
  [[ "$snapshot" == */Secrets/* && "$name" != SecretsMySqlDbContext ]] && continue
  rm -rf "$(dirname "$snapshot")"
done < <(find src -name '*ModelSnapshot.cs' -path '*/EntityFrameworkCore/Migrations/*' -not -path '*/obj/*')

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
for row in "${rows[@]}"; do
  IFS='|' read -r context provider assembly namespace <<<"$row"
  project="$(module_project "$assembly")"
  dir="$(dirname "$project")/$(output_dir "$context" "$provider" "$assembly")"
  if [[ "$assembly" == "Elsa.Secrets.Persistence.EntityFrameworkCore" && -d "$dir" ]]; then
    echo "skip $context: Secrets keeps its migration chain"
    continue
  fi
  rm -rf "$dir"
  targets+=("$context|$provider|$project|$(output_dir "$context" "$provider" "$assembly")|$namespace")
done

build_tooling

for target in "${targets[@]}"; do
  IFS='|' read -r context provider project dir namespace <<<"$target"
  echo "migrations add Initial --context $context"
  dotnet ef migrations add Initial \
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

echo "Regenerated ${#targets[@]} migration set(s). Build the modules and run the EF migration tests before committing."

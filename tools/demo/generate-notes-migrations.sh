#!/usr/bin/env bash
# Generates the Notes sample's EF migrations with dotnet-ef, through the sample's own design-time startup project
# (tools/demo/Elsa.Samples.Nuplane.Notes.Tooling), for both providers the sample supports.
#
# Usage:
#   bash tools/demo/generate-notes-migrations.sh initial   # release 1.0.0: the Initial migration and its model snapshot
#   bash tools/demo/generate-notes-migrations.sh tags      # release 1.1.0: the AddTags migration and its model snapshot
#
# The two releases are one project (see the project file), so the model changes between builds and each step builds
# the version it describes:
#   Migrations/Notes/<Provider>/      the Initial migration and the version-1 snapshot; compiled into both releases
#                                     (the version-1 snapshot only into release 1.0.0)
#   V2/Migrations/Notes/<Provider>/   AddTags and the version-2 snapshot; compiled into release 1.1.0 only
#
# `tags` generates against the version-1 snapshot, so it needs `initial` to have run first. Regenerating `initial`
# discards both sets, because a regenerated Initial has a new migration id.
#
# Never run tools/ef/generate-module-migrations.sh for this module: it regenerates the first-party modules, and this
# one is not part of that set.
set -euo pipefail

step="${1:-}"
[[ "$step" == "initial" || "$step" == "tags" ]] || { sed -n '2,8p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2; exit 2; }

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"
sample="samples/Elsa.Samples.Nuplane.Notes"
project="$sample/Elsa.Samples.Nuplane.Notes.csproj"
tooling="tools/demo/Elsa.Samples.Nuplane.Notes.Tooling/Elsa.Samples.Nuplane.Notes.Tooling.csproj"
configuration="${ELSA_EF_CONFIGURATION:-Release}"
providers=(Sqlite PostgreSql)

# A schema belongs to a deployment, not to a migration, and generation never opens a connection.
unset ELSA_EF_SCHEMA ELSA_EF_CONNECTION

dotnet tool restore >/dev/null

build_tooling() {
  local log
  log="$(mktemp)"
  if ! dotnet build "$tooling" -c "$configuration" -v q -nologo >"$log" 2>&1; then
    grep -E " error " "$log" | sort -u >&2 || cat "$log" >&2
    rm -f "$log"
    echo "Tooling build failed. A half-generated migration set may need deleting first." >&2
    exit 1
  fi
  rm -f "$log"
}

add_migration() {
  local name="$1" provider="$2" dir="$3"
  local namespace="Elsa.Samples.Nuplane.Notes.Migrations.Notes.$provider"
  echo "migrations add $name --context Notes${provider}DbContext"
  dotnet ef migrations add "$name" \
    --context "Notes${provider}DbContext" \
    --project "$project" \
    --startup-project "$tooling" \
    --output-dir "$dir" \
    --namespace "$namespace" \
    --configuration "$configuration" \
    --no-build >/dev/null
}

case "$step" in
  initial)
    export DemoVersion=1
    rm -rf "$sample/Migrations" "$sample/V2/Migrations"
    build_tooling
    for provider in "${providers[@]}"; do
      dir="Migrations/Notes/$provider"
      add_migration Initial "$provider" "$dir"
      # dotnet ef places a first snapshot by namespace rather than beside the migration; keep them together.
      snapshot="$(find "$sample/Elsa" -name "Notes${provider}DbContextModelSnapshot.cs" | head -n 1)"
      mv "$snapshot" "$sample/$dir/"
      bash tools/ef/strip-provider-calls.sh "$sample/$dir"
    done
    rm -rf "$sample/Elsa"
    ;;
  tags)
    export DemoVersion=2
    # The version-1 snapshot stays compiled in, so EF diffs the version-2 model against it.
    export DemoKeepV1Snapshot=true
    rm -rf "$sample/V2/Migrations"
    build_tooling
    for provider in "${providers[@]}"; do
      dir="V2/Migrations/Notes/$provider"
      snapshot="Notes${provider}DbContextModelSnapshot.cs"
      v1="$sample/Migrations/Notes/$provider/$snapshot"
      saved="$(mktemp)"
      cp "$v1" "$saved"
      add_migration AddTags "$provider" "$dir"
      # EF updates a snapshot where it finds it. The version-1 snapshot must stay what release 1.0.0 compiles, so the
      # updated one moves beside AddTags and the original goes back.
      if ! cmp -s "$saved" "$v1"; then
        mv "$v1" "$sample/$dir/$snapshot"
        mv "$saved" "$v1"
      else
        rm -f "$saved"
      fi
      bash tools/ef/strip-provider-calls.sh "$sample/$dir"
    done
    ;;
esac

echo "Generated the '$step' migration set. Build both releases (DemoVersion=1 and 2) before committing."

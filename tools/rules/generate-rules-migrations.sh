#!/usr/bin/env bash
# Add one named migration for the Rules SQLite context without replacing its retained migration set.
set -euo pipefail

fail() {
  echo "error: $*" >&2
  exit 2
}

usage() {
  cat <<'USAGE'
Usage:
  bash tools/rules/generate-rules-migrations.sh <MigrationName>

The only supported context is RulesSqliteDbContext. Migration files and the model snapshot
are retained under Extensions/Rules/src/Persistence/EntityFrameworkCore/Migrations/Rules/Sqlite/.
USAGE
}

if [[ $# -ne 1 ]]; then
  usage >&2
  exit 2
fi
if [[ "$1" == "-h" || "$1" == "--help" ]]; then
  usage
  exit 0
fi

migration_name="$1"
[[ "$migration_name" =~ ^[A-Za-z][A-Za-z0-9]*$ ]] ||
  fail "MigrationName must contain only ASCII letters and digits and start with a letter."

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"

context="RulesSqliteDbContext"
project_rel="Extensions/Rules/src/Persistence/EntityFrameworkCore/Elsa.Rules.Persistence.EntityFrameworkCore.csproj"
project="${project_rel}"
project_dir="$root/Extensions/Rules/src/Persistence/EntityFrameworkCore"
context_source="$project_dir/RulesSqliteDbContext.cs"
tooling="Extensions/Rules/src/Tooling/Elsa.Rules.Tooling.csproj"
factory_source="Extensions/Rules/src/Tooling/RulesSqliteDbContextFactory.cs"
output_dir="Migrations/Rules/Sqlite"
namespace="Elsa.Rules.Persistence.EntityFrameworkCore.Migrations.Rules.Sqlite"
destination="$project_dir/$output_dir"
expected_destination="$root/Extensions/Rules/src/Persistence/EntityFrameworkCore/Migrations/Rules/Sqlite"
configuration="${ELSA_EF_CONFIGURATION:-Release}"

[[ "$context" == "RulesSqliteDbContext" ]] ||
  fail "Unsupported context '$context'; only RulesSqliteDbContext is supported."
[[ "$output_dir" == "Migrations/Rules/Sqlite" && "$destination" == "$expected_destination" ]] ||
  fail "Rules migration output must be the module's Migrations/Rules/Sqlite directory."

for required_file in "$project_rel" "$context_source" "$tooling" "$factory_source" \
                     "tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactory.cs" \
                     "tools/ef/strip-provider-calls.sh"; do
  [[ -f "$required_file" ]] || fail "Required migration input is missing: $required_file."
done

grep -Fq 'internal sealed class RulesSqliteDbContext' "$context_source" ||
  fail "The supported SQLite context declaration is missing or has changed."
grep -Fq 'ModuleDesignTimeFactory<RulesSqliteDbContext>' "$factory_source" ||
  fail "The Rules tooling factory does not target the supported context."

# Refuse redirected destinations and non-directory path components before restore/build/scaffolding.
for path in "$project_dir/Migrations" "$project_dir/Migrations/Rules" "$destination"; do
  [[ ! -L "$path" ]] || fail "Migration path components must not be symlinks: $path."
  [[ ! -e "$path" || -d "$path" ]] || fail "Migration path component is not a directory: $path."
done

if [[ -d "$destination" ]] &&
   find "$destination" -maxdepth 1 -type f -name "*_${migration_name}.cs" -print -quit | grep -q .; then
  fail "A migration named '$migration_name' already exists for $context."
fi

snapshots="$(find "$project_dir" -type f -name "${context}ModelSnapshot.cs" \
  -not -path '*/obj/*' -not -path '*/bin/*' -print)"
snapshot_count="$(printf '%s\n' "$snapshots" | awk 'NF { count++ } END { print count + 0 }')"
[[ "$snapshot_count" -le 1 ]] ||
  fail "Expected at most one retained model snapshot for $context; found $snapshot_count."
if [[ -n "$snapshots" && "$snapshots" != "$destination/${context}ModelSnapshot.cs" ]]; then
  fail "The existing model snapshot is outside the supported destination: $snapshots."
fi

# EF design tooling must not bake a deployment-specific schema or connection into the snapshot.
unset ELSA_EF_SCHEMA ELSA_EF_CONNECTION

dotnet tool restore
dotnet build "$tooling" -c "$configuration" -v q -nologo

dotnet ef migrations add "$migration_name" \
  --context "$context" \
  --project "$project" \
  --startup-project "$tooling" \
  --output-dir "$output_dir" \
  --namespace "$namespace" \
  --configuration "$configuration" \
  --no-build

# EF places a first snapshot beside its namespace when no snapshot exists; keep the snapshot with this
# context's retained migration set, as the shared generator does.
snapshots="$(find "$project_dir" -type f -name "${context}ModelSnapshot.cs" \
  -not -path '*/obj/*' -not -path '*/bin/*' -print)"
snapshot_count="$(printf '%s\n' "$snapshots" | awk 'NF { count++ } END { print count + 0 }')"
[[ "$snapshot_count" -eq 1 ]] ||
  fail "Expected one model snapshot after scaffolding $context; found $snapshot_count."
if [[ "$snapshots" != "$destination/${context}ModelSnapshot.cs" ]]; then
  mv "$snapshots" "$destination/"
fi

bash tools/ef/strip-provider-calls.sh "$destination"

# The migration is provider implementation, not a public consumer contract. EF discovers internal
# migration classes with their default constructor. Keep future scaffolds consistent with this module.
python3 - "$destination" "$migration_name" <<'PYTHON'
import pathlib
import sys
folder, name = pathlib.Path(sys.argv[1]), sys.argv[2]
files = list(folder.glob(f"*_{name}.cs"))
if len(files) != 1:
    raise SystemExit("Expected exactly one scaffolded migration source")
path = files[0]
text = path.read_text(encoding="utf-8-sig")
old = f"public partial class {name} : Migration"
if text.count(old) != 1:
    raise SystemExit("Unexpected scaffolded migration class declaration; review output")
path.write_text("// <auto-generated />\n" + text.replace(old, f"internal partial class {name} : Migration"))
PYTHON

echo "Added '$migration_name' for $context under $destination. Review the migration and model snapshot before committing."

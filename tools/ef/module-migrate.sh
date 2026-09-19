#!/usr/bin/env bash
# Out-of-process migration apply, validation and SQL scripting for every first-party EF module context.
#
# Usage:
#   bash tools/ef/module-migrate.sh pending [context-regex]
#   bash tools/ef/module-migrate.sh apply <Sqlite|SqlServer|PostgreSql|MySql> <connection-string> [context-regex]
#   bash tools/ef/module-migrate.sh validate <Sqlite|SqlServer|PostgreSql|MySql> <connection-string> [context-regex]
#   bash tools/ef/module-migrate.sh script <Sqlite|SqlServer|PostgreSql|MySql> <output-dir> [context-regex]
#   bash tools/ef/module-migrate.sh script-check <Sqlite|SqlServer|PostgreSql|MySql> <output-dir> [context-regex]
#
# ELSA_EF_SCHEMA applies and scripts into the schema a host configured, the same one
# Elsa:Persistence:EntityFramework:Schema names. SQLite ignores it and MySQL refuses it. The schema itself
# needs no setting up: EF's migrations-history script creates it on both providers that take one.
#
# pending needs no database: it fails when a module model changed without a regenerated migration.
# apply runs `dotnet ef database update` per module context against one database; every module records its
# own history table, which is the one a host started with
# Elsa:Persistence:EntityFramework:Migrate:Policy=Validate reads.
# validate fails when any module context still has a pending migration in that database.
# script needs no database either: it writes one idempotent .sql per module context to
# <output-dir>/<Module>/<Provider>.sql, which is the artifact a DBA reviews and a pipeline runs.
# SQLite is refused there: EF cannot generate an idempotent script for it.
# script-check regenerates into a temporary directory and diffs it against <output-dir>, so a hand-edited
# script, a model change with no regenerated script, or a stale file fails instead of reaching a DBA.
# Secrets' historical SQLite, SQL Server and PostgreSQL chains are applied by tools/ef/dual-migrate.sh.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"
tooling="tools/ef/Elsa.EntityFrameworkCore.Tooling/Elsa.EntityFrameworkCore.Tooling.csproj"
# Required modules live under src/ and optional ones under extensions/ (#1815). Roots are filtered to
# the ones that exist: `find` exits non-zero on a missing directory, and under `set -e` that would end
# the run instead of reporting the one module it could not resolve.
module_roots=(src)
if [[ -d extensions ]]; then module_roots+=(extensions); fi
configuration="${ELSA_EF_CONFIGURATION:-Release}"

usage() {
  sed -n '4,9p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2
  exit 2
}

command="${1:-}"
case "$command" in
  pending) provider=""; connection=""; filter="${2:-.*}" ;;
  apply|validate) [[ $# -ge 3 ]] || usage; provider="$2"; connection="$3"; filter="${4:-.*}" ;;
  script|script-check) [[ $# -ge 3 ]] || usage; provider="$2"; connection=""; destination="$3"; filter="${4:-.*}" ;;
  *) usage ;;
esac

# EF cannot express an idempotent script for SQLite: SqliteHistoryRepository.GetEndIfScript throws
# NotSupportedException, because SQLite has no conditional statement to wrap a migration in. A plain
# script would sit in the same tree looking identical while being unsafe to re-run, so refuse instead.
if [[ "$command" == script* && "$provider" == "Sqlite" ]]; then
  echo "$command: SQLite cannot produce an idempotent script (EF throws NotSupportedException)." >&2
  echo "Script a server provider, and bring a SQLite database up to date with:" >&2
  echo "  bash tools/ef/module-migrate.sh apply Sqlite \"<connection>\"" >&2
  exit 2
fi

# `dotnet ef --output` resolves against its own working directory, so scripts are written to absolute paths.
case "$command" in
  script)
    mkdir -p "$destination"
    destination="$(cd "$destination" && pwd -P)"
    generated="$destination"
    ;;
  script-check)
    [[ -d "$destination" ]] || { echo "No such directory: $destination" >&2; exit 2; }
    destination="$(cd "$destination" && pwd -P)"
    generated="$(mktemp -d)"
    trap 'rm -rf "$generated"' EXIT
    ;;
esac

dotnet tool restore >/dev/null
log="$(mktemp)"
if ! dotnet build "$tooling" -c "$configuration" -v q -nologo >"$log" 2>&1; then
  grep -E " error " "$log" | sort -u >&2 || cat "$log" >&2
  exit 1
fi
rm -f "$log"

ef() {
  local context="$1" project="$2"
  shift 2
  dotnet ef "$@" --context "$context" --project "$project" --startup-project "$tooling" \
    --configuration "$configuration" --no-build
}

failed=0
count=0
while IFS='|' read -r context row_provider assembly _; do
  [[ "$context" =~ ^($filter)$ ]] || continue
  [[ -z "$provider" || "$row_provider" == "$provider" ]] || continue
  project="$(find "${module_roots[@]}" -name "$assembly.csproj" -not -path '*/obj/*' | head -n 1)"
  count=$((count + 1))
  case "$command" in
    pending)
      if ! ef "$context" "$project" migrations has-pending-model-changes >/dev/null 2>&1; then
        echo "pending model changes: $context" >&2
        failed=1
      fi
      ;;
    apply)
      echo "apply $context"
      ELSA_EF_CONNECTION="$connection" ef "$context" "$project" database update >/dev/null
      ;;
    validate)
      pending="$(ELSA_EF_CONNECTION="$connection" ef "$context" "$project" migrations list --no-color 2>/dev/null | grep -F '(Pending)' || true)"
      if [[ -n "$pending" ]]; then
        echo "pending migrations for $context: $pending" >&2
        failed=1
      fi
      ;;
    script|script-check)
      # The same <Module>/<Provider> split the migrations themselves are stored under.
      relative="${context%"$row_provider"DbContext}/$row_provider.sql"
      mkdir -p "$(dirname "$generated/$relative")"
      ef "$context" "$project" migrations script --idempotent --output "$generated/$relative" >/dev/null
      if [[ "$command" == script ]]; then
        echo "script $context -> ${destination#"$root/"}/$relative"
      elif [[ ! -f "$destination/$relative" ]]; then
        echo "missing script: ${destination#"$root/"}/$relative" >&2
        failed=1
      elif ! diff -u "$destination/$relative" "$generated/$relative" >&2; then
        echo "out of date: ${destination#"$root/"}/$relative" >&2
        failed=1
      fi
      ;;
  esac
done < <(dotnet run --project "$tooling" -c "$configuration" --no-build -- list)

# A module that was removed or renamed leaves a script nobody generates any more; only a full check can
# tell that from a context the caller deliberately filtered out.
if [[ "$command" == script-check && "$filter" == ".*" ]]; then
  while IFS= read -r stale; do
    [[ -n "$stale" ]] || continue
    echo "stale script: ${destination#"$root/"}/${stale#./}" >&2
    failed=1
  done < <(comm -13 \
    <(cd "$generated" && find . -name "$provider.sql" | sort) \
    <(cd "$destination" && find . -name "$provider.sql" | sort))
fi

if [[ $count -eq 0 ]]; then
  echo "No module context matched." >&2
  exit 2
fi
if [[ $failed -ne 0 ]]; then
  exit 1
fi
echo "$command: $count module context(s) OK"

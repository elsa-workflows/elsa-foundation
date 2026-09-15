#!/usr/bin/env bash
# Out-of-process migration apply and validation for every first-party EF module context.
#
# Usage:
#   bash tools/ef/module-migrate.sh pending [context-regex]
#   bash tools/ef/module-migrate.sh apply <Sqlite|SqlServer|PostgreSql|MySql> <connection-string> [context-regex]
#   bash tools/ef/module-migrate.sh validate <Sqlite|SqlServer|PostgreSql|MySql> <connection-string> [context-regex]
#
# pending needs no database: it fails when a module model changed without a regenerated migration.
# apply runs `dotnet ef database update` per module context against one database; every module records its
# own history table, which is the one the host's validate policy (EfMigrateOptions.Policy = Validate) reads.
# validate fails when any module context still has a pending migration in that database.
# Secrets' historical SQLite, SQL Server and PostgreSQL chains are applied by tools/ef/dual-migrate.sh.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"
tooling="tools/ef/Elsa.EntityFrameworkCore.Tooling/Elsa.EntityFrameworkCore.Tooling.csproj"
configuration="${ELSA_EF_CONFIGURATION:-Release}"

usage() {
  sed -n '4,7p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2
  exit 2
}

command="${1:-}"
case "$command" in
  pending) provider=""; connection=""; filter="${2:-.*}" ;;
  apply|validate) [[ $# -ge 3 ]] || usage; provider="$2"; connection="$3"; filter="${4:-.*}" ;;
  *) usage ;;
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
  project="$(find src -name "$assembly.csproj" -not -path '*/obj/*' | head -n 1)"
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
  esac
done < <(dotnet run --project "$tooling" -c "$configuration" --no-build -- list)

if [[ $count -eq 0 ]]; then
  echo "No module context matched." >&2
  exit 2
fi
if [[ $failed -ne 0 ]]; then
  exit 1
fi
echo "$command: $count module context(s) OK"

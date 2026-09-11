#!/usr/bin/env bash
# Nuplane dual-migrate CI/ops tool for the Secrets EF pilot.
#
# Applies compiled migrations from the module assembly Nuplane loads at runtime, and
# fails if a derived context's model drifted from its snapshot. Does not boot the host.
#
# Usage:
#   bash tools/ef/dual-migrate.sh pending
#   bash tools/ef/dual-migrate.sh apply [--sqlite|--sqlserver|--postgresql|--all]
#   bash tools/ef/dual-migrate.sh all
#   bash tools/ef/dual-migrate.sh            # same as all
#
# Environment (apply):
#   ELSA_SECRETS_EF_SQLITE      Sqlite connection. Default: a temp file.
#   ELSA_SECRETS_EF_SQLSERVER   Required to apply the SqlServer context.
#   ELSA_SECRETS_EF_POSTGRESQL  Required to apply the PostgreSql context.
#   ELSA_SECRETS_EF_REQUIRE_ALL=1  Fail when a non-Sqlite connection is missing.
#
# Hooks (per derived context):
#   dotnet ef database update --context <Derived>
#   dotnet ef migrations has-pending-model-changes --context <Derived>
# Factories read ELSA_SECRETS_EF_* so connection strings stay off the process command line.
set -euo pipefail

# shellcheck source=secrets-ef-lib.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)/secrets-ef-lib.sh"
secrets_ef_init
cd "$secrets_ef_root"

usage() {
  cat <<'EOF'
Usage:
  bash tools/ef/dual-migrate.sh pending
  bash tools/ef/dual-migrate.sh apply [--sqlite|--sqlserver|--postgresql|--all]
  bash tools/ef/dual-migrate.sh all

pending  Fail if any derived Secrets context has pending model changes.
         No database required.

apply    Apply compiled migrations for the selected derived contexts.
         Sqlite uses ELSA_SECRETS_EF_SQLITE or a temp file.
         --sqlserver / --postgresql fail when the matching env is unset.
         --all skips a missing non-Sqlite env unless ELSA_SECRETS_EF_REQUIRE_ALL=1.

all      pending, then apply (default).
EOF
}

ef_common=(
  --project "$secrets_ef_module"
  --startup-project "$secrets_ef_startup"
)

run_pending() {
  local row context
  echo "Secrets EF: fail-if-pending model changes for each derived context"
  for row in "${secrets_ef_contexts[@]}"; do
    secrets_ef_split "$row"
    context="$secrets_ef_context"
    echo "  has-pending-model-changes --context $context"
    secrets_ef migrations has-pending-model-changes --context "$context" "${ef_common[@]}"
  done
}

sqlite_temp_file=""

# Factories treat whitespace as unset (SecretsDesignTimeConnection.Resolve). Match that
# here so " " cannot skip the required-env error or write elsa-secrets-design.db.
env_is_set() {
  local value="${1:-}"
  [[ -n "${value//[[:space:]]/}" ]]
}

cleanup_sqlite_temp() {
  if [[ -n "${sqlite_temp_file:-}" ]]; then
    rm -f "$sqlite_temp_file" "${sqlite_temp_file}-wal" "${sqlite_temp_file}-shm"
  fi
}

ensure_sqlite_env() {
  if env_is_set "${ELSA_SECRETS_EF_SQLITE:-}"; then
    return
  fi
  unset ELSA_SECRETS_EF_SQLITE
  # BSD mktemp requires the X's at the end of the template.
  sqlite_temp_file="$(mktemp "${TMPDIR:-/tmp}/elsa-secrets-ef.XXXXXX")"
  export ELSA_SECRETS_EF_SQLITE="Data Source=${sqlite_temp_file}"
  trap cleanup_sqlite_temp EXIT
}

apply_one() {
  local context="$1"
  local env_name="$2"
  local want="$3"

  case "$context" in
    SecretsSqliteDbContext)
      ensure_sqlite_env
      ;;
    *)
      if ! env_is_set "${!env_name:-}"; then
        # Skip-without-env is only for --all / default. An explicit engine must fail
        # so callers cannot treat a no-op as "this engine was updated".
        if [[ "$want" != "all" || "${ELSA_SECRETS_EF_REQUIRE_ALL:-}" == "1" ]]; then
          echo "error: $env_name is required to apply $context" >&2
          exit 1
        fi
        echo "  skip $context ($env_name unset)"
        return
      fi
      ;;
  esac

  echo "  database update --context $context"
  # Factories read ELSA_SECRETS_EF_*. Do not pass --connection: that puts credentials
  # on the process command line.
  secrets_ef database update --context "$context" "${ef_common[@]}"
}

run_apply() {
  local want="${1:-all}"
  local row
  echo "Secrets EF: apply compiled migrations from the Nuplane module assembly"
  for row in "${secrets_ef_contexts[@]}"; do
    secrets_ef_split "$row"
    case "$want" in
      all) ;;
      sqlite)
        [[ "$secrets_ef_context" == SecretsSqliteDbContext ]] || continue
        ;;
      sqlserver)
        [[ "$secrets_ef_context" == SecretsSqlServerDbContext ]] || continue
        ;;
      postgresql)
        [[ "$secrets_ef_context" == SecretsPostgreSqlDbContext ]] || continue
        ;;
      *)
        echo "error: unknown apply selector '$want'" >&2
        usage >&2
        exit 2
        ;;
    esac
    apply_one "$secrets_ef_context" "$secrets_ef_env" "$want"
  done
}

cmd="${1:-all}"
shift || true
selector="all"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --sqlite) selector="sqlite" ;;
    --sqlserver) selector="sqlserver" ;;
    --postgresql) selector="postgresql" ;;
    --all) selector="all" ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "error: unknown argument '$1'" >&2
      usage >&2
      exit 2
      ;;
  esac
  shift
done

case "$cmd" in
  pending)
    run_pending
    ;;
  apply)
    run_apply "$selector"
    ;;
  all)
    run_pending
    run_apply "$selector"
    ;;
  -h|--help)
    usage
    ;;
  *)
    echo "error: unknown command '$cmd'" >&2
    usage >&2
    exit 2
    ;;
esac

# Shared Secrets EF paths and derived contexts. Sourced by generate-ef-migrations.sh
# and dual-migrate.sh. Not executable on its own.

# shellcheck shell=bash

secrets_ef_init() {
  secrets_ef_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
  secrets_ef_module="src/Elsa/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"
  secrets_ef_startup="src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj"

  # context|output-dir|namespace|env-var
  secrets_ef_contexts=(
    "SecretsSqliteDbContext|Migrations/Sqlite|Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.Sqlite|ELSA_SECRETS_EF_SQLITE"
    "SecretsSqlServerDbContext|Migrations/SqlServer|Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.SqlServer|ELSA_SECRETS_EF_SQLSERVER"
    "SecretsPostgreSqlDbContext|Migrations/PostgreSql|Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.PostgreSql|ELSA_SECRETS_EF_POSTGRESQL"
  )
}

secrets_ef_split() {
  local row="$1"
  IFS='|' read -r secrets_ef_context secrets_ef_output_dir secrets_ef_namespace secrets_ef_env <<<"$row"
}

secrets_ef() {
  # Prefer a restored local tool, then PATH. CI/ops: `dotnet tool restore` from the repo root.
  if [[ -x "$secrets_ef_root/.tools/dotnet-ef" ]]; then
    "$secrets_ef_root/.tools/dotnet-ef" "$@"
    return
  fi
  if command -v dotnet-ef >/dev/null 2>&1; then
    dotnet-ef "$@"
    return
  fi
  dotnet ef "$@"
}

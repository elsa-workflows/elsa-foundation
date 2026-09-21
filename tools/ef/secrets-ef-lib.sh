# Shared Secrets EF paths and derived contexts. Sourced by generate-ef-migrations.sh.
# Not executable on its own.

# shellcheck shell=bash

secrets_ef_init() {
  secrets_ef_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
  secrets_ef_module="src/essentials/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"
  # The shared design-time startup project (#1878): it holds every provider engine and a
  # design-time factory per provider-derived context, including all four Secrets ones. The
  # Secrets-only Tooling project it replaced was retired with dual-migrate.sh.
  secrets_ef_startup="tools/ef/Elsa.EntityFrameworkCore.Tooling/Elsa.EntityFrameworkCore.Tooling.csproj"

  # context|output-dir|namespace
  secrets_ef_contexts=(
    "SecretsSqliteDbContext|Migrations/Sqlite|Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.Sqlite"
    "SecretsSqlServerDbContext|Migrations/SqlServer|Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.SqlServer"
    "SecretsPostgreSqlDbContext|Migrations/PostgreSql|Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.PostgreSql"
  )
}

secrets_ef_split() {
  local row="$1"
  IFS='|' read -r secrets_ef_context secrets_ef_output_dir secrets_ef_namespace <<<"$row"
}

secrets_ef() {
  # An explicit repository tool-path is authoritative.
  if [[ -x "$secrets_ef_root/.tools/dotnet-ef" ]]; then
    "$secrets_ef_root/.tools/dotnet-ef" "$@"
    return
  fi
  # The repository manifest is authoritative before any global PATH tool. Run `dotnet tool
  # restore` from the repo root first; a stale global dotnet-ef must not silently win.
  if [[ -f "$secrets_ef_root/.config/dotnet-tools.json" ]]; then
    dotnet ef "$@"
    return
  fi
  if command -v dotnet-ef >/dev/null 2>&1; then
    dotnet-ef "$@"
    return
  fi
  dotnet ef "$@"
}

#!/usr/bin/env bash
set -euo pipefail
# Stub: Secrets EF Initial migrations are generated from the Tooling project into the module.
# Full generate-ef-migrations driver (all modules × providers) is Phase 2.
#
# Usage (Secrets):
#   bash tools/ef/generate-ef-migrations.sh
#
# Requires the dotnet-ef tool (10.0.x).

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root"

module="src/Elsa/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"
startup="src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj"

ef() {
  dotnet ef "$@"
}

echo "Phase 2 stub: this will fail if an Initial migration already exists (dotnet ef rejects the duplicate name)."
echo "Regeneration that remove/replaces existing Initial sets is Phase 2. Review provider snapshots before committing."

ef migrations add Initial --context SecretsSqliteDbContext --project "$module" --startup-project "$startup" --output-dir Migrations/Sqlite --namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.Sqlite
ef migrations add Initial --context SecretsSqlServerDbContext --project "$module" --startup-project "$startup" --output-dir Migrations/SqlServer --namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.SqlServer
ef migrations add Initial --context SecretsPostgreSqlDbContext --project "$module" --startup-project "$startup" --output-dir Migrations/PostgreSql --namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.PostgreSql

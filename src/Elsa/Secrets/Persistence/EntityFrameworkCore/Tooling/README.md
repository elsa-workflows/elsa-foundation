# Secrets EF design-time tooling

This project exists so `dotnet ef` can see Sqlite, SqlServer, and Npgsql providers plus
`Microsoft.EntityFrameworkCore.Design`. It is **not** a Nuplane runtime package.

Generated migrations live in the **module** assembly
(`../Migrations/Sqlite|SqlServer|PostgreSql`) so `MigrateAsync` and
`tools/ef/dual-migrate.sh` can apply them without loading this tooling project at runtime.

## Generate

From the repository root (see also `tools/ef/README.md`):

```bash
dotnet tool restore
bash tools/ef/generate-ef-migrations.sh <MigrationName>
```

Equivalent per-context commands:

```bash
dotnet ef migrations add <MigrationName> \
  --context SecretsSqliteDbContext \
  --project src/Elsa/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj \
  --startup-project src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj \
  --output-dir Migrations/Sqlite \
  --namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.Sqlite
```

Repeat with `SecretsSqlServerDbContext` / `Migrations/SqlServer` and
`SecretsPostgreSqlDbContext` / `Migrations/PostgreSql`.

Factories set `MigrationsHistoryTable(__EFMigrationsHistory_ElsaSecrets)`. Connection strings
come from `ELSA_SECRETS_EF_SQLITE`, `ELSA_SECRETS_EF_SQLSERVER`, and
`ELSA_SECRETS_EF_POSTGRESQL` when set; `dotnet ef --connection` still overrides.

If a provider snapshot emits `*ModelBuilderExtensions` calls that need the provider
package to compile, rewrite them to Relational `HasColumnType` / annotations so the
module stays provider-free. Do not add SqlServer/Npgsql PackageReferences to the module.

## Dual-migrate (out of process / CI)

```bash
bash tools/ef/dual-migrate.sh pending   # has-pending-model-changes per derived context
bash tools/ef/dual-migrate.sh apply     # database update; Sqlite always, others if env set
bash tools/ef/dual-migrate.sh all
```

This is the Phase 2 Nuplane dual-migrate path: apply + fail-if-pending against the
module assembly Nuplane loads. It does not switch Workbench or accept ADR 0072.

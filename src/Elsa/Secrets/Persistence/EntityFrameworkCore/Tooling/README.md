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

The repository manifest pins `dotnet-ef` to 10.0.10. The shared helper first honors an explicit
executable `.tools/dotnet-ef`, then invokes the restored manifest through `dotnet ef`; a global
`dotnet-ef` is only a fallback when no repository manifest exists. Restore the manifest before
generation or dual-migrate runs so a stale global tool cannot select a different EF version.

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

## Migration and deployment safety

Each provider has an independent migration set and snapshot. Review all three generated outputs;
`migrations has-pending-model-changes` detects source model/snapshot drift, while
`database update` and runtime `MigratePolicy=Validate` use the database's
`__EFMigrationsHistory_ElsaSecrets` to detect unapplied compiled migrations. One check does not
replace the other.

Use the deployment sequence in [tools/ef/README.md](../../../../../../tools/ef/README.md): back
up and quiesce writes, run `pending`, select exactly one provider and apply it with a short-lived
least-privilege migration identity, verify migration history and table shape, then start the application with
`MigratePolicy=Validate`. Keep `SecretsGroundworkPersistence` disabled when this feature is
enabled. A failed apply stops the rollout; inspect migration history and table shape before retrying.

Prefer additive/expand-contract migrations so an application rollback can be considered
separately from schema recovery. Do not blindly invoke a down-migration. In particular,
`WidenLookupKeys.Down` narrows SQL Server/PostgreSQL columns from unbounded text to 64 characters,
which is unsafe when longer lookup keys exist; use a verified backup restore or a forward fix
instead.

If a provider snapshot emits `*ModelBuilderExtensions` calls that need the provider
package to compile, rewrite them to Relational `HasColumnType` / annotations so the
module stays provider-free. Do not add SqlServer/Npgsql PackageReferences to the module.

## Dual-migrate (out of process / CI)

```bash
bash tools/ef/dual-migrate.sh pending   # has-pending-model-changes per derived context
bash tools/ef/dual-migrate.sh apply     # database update; --all skips missing non-Sqlite env
bash tools/ef/dual-migrate.sh apply --sqlserver   # fails when ELSA_SECRETS_EF_SQLSERVER is unset
bash tools/ef/dual-migrate.sh all
```

This is the Phase 2 Nuplane dual-migrate path: apply + fail-if-pending against the
module assembly Nuplane loads. It does not switch Workbench or accept ADR 0072.

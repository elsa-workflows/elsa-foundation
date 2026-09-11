# generate-ef-migrations / dual-migrate

Convention for first-party EF modules under proposed ADR 0072 (Secrets pilot).

## Layout

- **Module package** (`src/Elsa/<Domain>/Persistence/EntityFrameworkCore/`): `*DbContext` + derived
  provider contexts + `Migrations/<Provider>/`. References EF Core + Relational only. This is the
  assembly Nuplane loads at apply time.
- **Tooling project** (`.../EntityFrameworkCore/Tooling/`): `IDesignTimeDbContextFactory` per
  derived context, provider PackageReferences, `Microsoft.EntityFrameworkCore.Design`.
- **Policy package** (`src/Elsa/Persistence/EntityFramework/`): history table name, provider guard,
  AutoMigrate vs Validate. No provider engines.

## Command shape

Always pass `--startup-project` as the tooling project and `--project` as the module so generated
files compile into the assembly Nuplane loads at apply time.

History table: `EfMigrationsHistory.TableName("<Module>")` → `__EFMigrationsHistory_<Module>`.
Set it on the factory's `Use*` options (`MigrationsHistoryTable`), not as a magic base-context property.

## Secrets

See `src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/README.md`.

```bash
dotnet tool restore   # installs dotnet-ef 10.0.x from .config/dotnet-tools.json
# or: dotnet tool install dotnet-ef --version 10.0.10 --tool-path .tools

bash tools/ef/generate-ef-migrations.sh <MigrationName>
bash tools/ef/dual-migrate.sh pending
bash tools/ef/dual-migrate.sh apply --sqlite
bash tools/ef/dual-migrate.sh all
```

## Dual-migrate (out of process / CI)

`tools/ef/dual-migrate.sh` is the Nuplane dual-migrate CI tool. It does not boot the host.

| Command | Hook | Needs a database |
|---|---|---|
| `pending` | `dotnet ef migrations has-pending-model-changes --context <Derived>` | no |
| `apply` | `dotnet ef database update --context <Derived> --connection <cs>` | yes |

Both run for each derived Secrets context (`SecretsSqliteDbContext`,
`SecretsSqlServerDbContext`, `SecretsPostgreSqlDbContext`).

Apply connections:

- Sqlite: `ELSA_SECRETS_EF_SQLITE` or a temp file.
- SqlServer: `ELSA_SECRETS_EF_SQLSERVER` (skipped when unset unless `ELSA_SECRETS_EF_REQUIRE_ALL=1`).
- PostgreSql: `ELSA_SECRETS_EF_POSTGRESQL` (same skip / require rule).

`pending` is the CI-safe check for all three providers. Wire `bash tools/ef/dual-migrate.sh pending`
into a workflow only when a job already restores `dotnet-ef`; the current Build & test job does not.

Runtime (in-process) apply after CShells enable or reload uses the same `EfMigratePolicy` /
`EfDatabaseMigrator` path as a plain host. That is not a substitute for this out-of-process tool.

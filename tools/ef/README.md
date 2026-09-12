# generate-ef-migrations / dual-migrate

Convention for first-party EF modules under accepted
[ADR 0073](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md).
The current commands began with the Secrets pilot and must become module-parameterized under #1657.

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
dotnet tool restore   # restores the pinned dotnet-ef 10.0.10 from .config/dotnet-tools.json
# or: dotnet tool install dotnet-ef --version 10.0.10 --tool-path .tools

bash tools/ef/generate-ef-migrations.sh <MigrationName>
bash tools/ef/dual-migrate.sh pending
bash tools/ef/dual-migrate.sh apply --sqlite
bash tools/ef/dual-migrate.sh all
```

The helper resolves EF tooling in this order: an executable repository-local `.tools/dotnet-ef`,
the repository-manifest `dotnet ef` after `dotnet tool restore`, and only then a `dotnet-ef`
executable on `PATH`. This prevents a stale global tool from silently overriding the repository's
pinned version. The explicit `--tool-path .tools` installation is useful for a clean operator
checkout; the manifest path is the normal CI path.

## Dual-migrate (out of process / CI)

`tools/ef/dual-migrate.sh` is the Nuplane dual-migrate CI tool. It does not boot the host.

| Command | Hook | Needs a database |
|---|---|---|
| `pending` | `dotnet ef migrations has-pending-model-changes --context <Derived>` | no |
| `apply` | `dotnet ef database update`, then managed legacy-projection reindex for `<Derived>` (factories read `ELSA_SECRETS_EF_*`) | yes |

Both run for each derived Secrets context (`SecretsSqliteDbContext`,
`SecretsSqlServerDbContext`, `SecretsPostgreSqlDbContext`).

Apply connections:

- Sqlite: `ELSA_SECRETS_EF_SQLITE` or a temp file.
- SqlServer: `ELSA_SECRETS_EF_SQLSERVER`. Required for `apply --sqlserver`. Under
  `--all` / default, skipped when unset unless `ELSA_SECRETS_EF_REQUIRE_ALL=1`.
- PostgreSql: `ELSA_SECRETS_EF_POSTGRESQL` (same explicit-vs-all rule).

`ELSA_SECRETS_EF_CONFIGURATION` selects the MSBuild configuration used for the one tooling build
and every EF call; it defaults to `Release`. The script builds the tooling project once, then
passes that configuration and `--no-build` to each EF command. A caller that has already built
the same checkout and configuration may set `ELSA_SECRETS_EF_SKIP_BUILD=1` to avoid competing
writes to loaded build outputs during a parallel test or CI process. That explicit caller assumes
responsibility for artifact freshness; clean operator checkouts should keep the default one-build
safety net. The repository test harness derives the active Debug/Release configuration from its
assembly and declares the Tooling project as a build dependency before opting into this mode.

`pending` is the CI-safe check for all three providers. The current Build & test job restores
repository-local tools before build and test; if that job invokes this check, no extra restore is
needed. Any other job must run `dotnet tool restore` (or install the explicit `.tools` tool) first.

## What the checks mean

The checks cover different failure classes:

- `pending` runs `migrations has-pending-model-changes` for every derived context. It compares the
  current EF model with that context's committed model snapshot and does not inspect database
  history. A failure means a migration is missing from source; it is not proof that a database has
  unapplied migrations.
- `apply` runs `database update` against the selected provider, applies compiled migrations
  missing from `__EFMigrationsHistory_ElsaSecrets`, then reindexes projection fields in bounded
  transactions. The repair targets fields
  written by the pre-contract host-runtime casing algorithm, derives them from
  the stored document, preserves concurrency tokens, and is idempotent. SQL Server and PostgreSQL require their
  provider-specific connection environment variable and a reachable database. SQLite uses
  `ELSA_SECRETS_EF_SQLITE` when set; otherwise the script creates a temporary database and removes
  it on exit, which validates the artifact but does not update a deployment database.
- Runtime `MigratePolicy=Validate` calls EF's pending-database-migration check and fails closed
  when the database history is behind. Both runtime policies then audit the pinned projection
  contract and fail closed when operator reindex is still required. Neither runtime path rewrites
  legacy rows, and neither replaces the source/model `pending` check.

## Deployment and rollback boundary

Use an explicit provider selector (`apply --sqlite`, `apply --sqlserver`, or
`apply --postgresql`) and set its matching `ELSA_SECRETS_EF_*` connection for the target database.
Omitting `ELSA_SECRETS_EF_SQLITE` intentionally targets only the disposable fallback. Before
applying to a deployment database, take a backup, quiesce writes, and run `pending`. Use a short-lived least-privilege deployment identity
with the DDL rights needed for that provider; after the schema is verified, run the application
with its least-privilege runtime identity. Do not enable `SecretsEntityFrameworkCore` together
with `SecretsGroundworkPersistence` in the same shell. Groundwork Secrets provider-matrix
and ledger-growth obligations apply to the Groundwork-selected composition only (#1631);
this out-of-process EF path is proven independently of that matrix.

After `apply` succeeds, verify the selected database's
`__EFMigrationsHistory_ElsaSecrets` contains the expected migration IDs and that the expected
`elsa_secrets` table shape is present before deploying the application with
`MigratePolicy=Validate`. If `pending` or `apply` fails, stop the rollout and inspect both the
provider schema and history before retrying; a failed command is not deployment proof.

Prefer additive/expand-contract schema changes so an application binary can be rolled back only
after compatibility is checked. Do not blindly run a down-migration as an application rollback:
`WidenLookupKeys.Down` narrows SQL Server/PostgreSQL lookup columns back to 64 characters and can
fail or lose values longer than that. Use a verified backup restore or a forward migration for
schema recovery; treat application rollback and schema recovery as separate decisions.

Runtime (in-process) apply after CShells enable or reload uses the same `EfMigratePolicy` /
`EfDatabaseMigrator` path as a plain host. That is not a substitute for this out-of-process tool.

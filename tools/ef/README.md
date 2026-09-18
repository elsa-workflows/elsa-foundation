# generate-ef-migrations / dual-migrate

Convention for first-party EF modules under accepted
[ADR 0073](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md).

## Every module (#1669)

Every first-party EF module ships one migration set per provider (Sqlite, SqlServer, PostgreSql, MySql),
compiled into the module assembly under `Migrations/<Context>/<Provider>/`, with its own history table.
`tools/ef/Elsa.EntityFrameworkCore.Tooling` is the shared design-time startup project: it holds the provider
engines and one `ModuleDesignTimeFactory` line per provider-derived context. Add a line there when a module
gains a context.

```bash
bash tools/ef/generate-module-migrations.sh [context-regex]   # regenerate Initial after a model change
bash tools/ef/module-migrate.sh pending                        # CI-safe: fails on an unmigrated model change
bash tools/ef/module-migrate.sh apply PostgreSql "<connection>" # out-of-process apply, every module
bash tools/ef/module-migrate.sh validate PostgreSql "<connection>"
bash tools/ef/module-migrate.sh script PostgreSql db/migrations  # reviewable SQL, no database needed
bash tools/ef/module-migrate.sh script-check PostgreSql db/migrations # CI-safe: fails on edited/stale SQL
```

Elsa is pre-release with no production data, so a module keeps a single `Initial` migration per provider
that is regenerated whenever its model changes; Secrets keeps its historical chain. The generator rewrites
the few provider-package calls EF emits so modules stay provider-free.

At runtime each EF shell feature registers `EfModuleMigrator<TContext>` for its context. It runs on shell
activation (`IShellInitializer`) and plain host start, applying migrations under `EfMigrateOptions.Policy =
AutoMigrate` (the default) or refusing a stale database under `Validate`, which is the mode to use when an
operator applies migrations out of process first. All Runtime participants share one context and one
`__EFMigrationsHistory_ElsaRuntime` history. `tests/Elsa/Persistence/EntityFrameworkCore/Migrations` proves
the model/migration match on all four providers and a fresh install of every module into one database on
SQLite (fast gate) and on SQL Server, PostgreSQL and MySQL (Testcontainers).

That policy is configuration, not code: set `Elsa:Persistence:EntityFramework:Migrate:Policy` (environment
variable `Elsa__Persistence__EntityFramework__Migrate__Policy`) to `Validate` in the deployment a pipeline
migrates, and leave it unset everywhere else. A value that names neither policy fails the host rather than
falling back to auto-migrate. See
[src/Elsa/Persistence/EntityFramework/README.md](../../src/Elsa/Persistence/EntityFramework/README.md#choosing-the-policy-operator-setting).

## Reviewable SQL scripts (`script` / `script-check`)

A DBA-controlled pipeline does not run `database update` against production; it reviews SQL and runs it
itself. `script` produces exactly that, from the same context enumeration every other command uses:

```bash
bash tools/ef/module-migrate.sh script SqlServer db/migrations
bash tools/ef/module-migrate.sh script PostgreSql db/migrations
bash tools/ef/module-migrate.sh script MySql db/migrations
```

**Layout: `<output-dir>/<Module>/<Provider>.sql`** — one file per module context and provider, under the
same `<Module>/<Provider>` split the compiled migrations use (`Migrations/<Module>/<Provider>/`), so a
reviewer reads the same tree in both places. `<Module>` is the context name without its provider suffix
(`RuntimeSqlServerDbContext` → `Runtime/SqlServer.sql`).

Every file is generated with `--idempotent`, which means:

- It is **safe to re-run**: each migration in it is wrapped in a check against that module's own
  migrations-history table, so a migration already recorded there is skipped rather than re-applied.
- It **records what it applied** into that same per-module `__EFMigrationsHistory_*` table — Runtime's is
  `__EFMigrationsHistory_ElsaRuntime`, Activities Design's is `__EFMigrationsHistory_activities_design`,
  and each script names its own in its first statement. That is the table a host started with
  `Elsa:Persistence:EntityFramework:Migrate:Policy=Validate` reads when it decides whether the database is
  up to date, so applying the script and starting the host in `Validate` agree by construction.
- It needs **no database to generate**: the design-time factories bind a placeholder connection unless
  `ELSA_EF_CONNECTION` names a real one, and scripting never opens it.

**SQLite is refused, not scripted.** EF cannot generate an idempotent script for SQLite
(`SqliteHistoryRepository.GetEndIfScript` throws `NotSupportedException`, because SQLite has no
conditional statement to wrap a migration in), and a plain script would sit in the same tree looking like
every other file while being unsafe to re-run. `script Sqlite` therefore exits 2 and says so. A SQLite
database is brought up to date with `module-migrate.sh apply Sqlite "<connection>"`, or by a host on the
`AutoMigrate` default.

Secrets ships only a MySQL context in this catalog; its historical SQLite, SQL Server and PostgreSQL
chains belong to `tools/ef/dual-migrate.sh`.

### Keeping the committed SQL honest

`script-check` regenerates into a temporary directory and diffs against the directory you pass. It checks
every module context and then exits non-zero if any file differed, was missing, or was stale — a file no
module context generates any more (that last check runs only for a full, unfiltered check). It prints the
unified diff, so the failure says which statement moved:

```bash
bash tools/ef/module-migrate.sh script-check PostgreSql db/migrations
```

A CI job would call exactly that, once per server provider, after `dotnet tool restore` — nothing else is
wired up here. It catches the two silent failures that matter: SQL hand-edited after review, and a model
change merged without a regenerated script. The second one is caught because the command builds the
tooling project — and with it every module — before it scripts anything, so it compares against the
current model, not a stale assembly.

### Where the SQL is committed

This repository does not commit generated `.sql`. A team that reviews SQL commits the tree the commands
above write — conventionally `db/migrations/<Module>/<Provider>.sql` — because that is what makes the
change reviewable: the schema diff shows up in the pull request next to the model change that caused it,
a DBA approves the statements before anything runs, and `script-check` in CI proves the committed file is
still the file the model generates.

## Secrets pilot tooling

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
with its least-privilege runtime identity.

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

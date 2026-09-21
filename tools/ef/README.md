# EF migration tooling

Convention for first-party EF modules under accepted
[ADR 0073](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md).

## Every module (#1669)

Every first-party EF module ships one migration set per provider (Sqlite, SqlServer, PostgreSql, MySql),
compiled into the module assembly under `Migrations/<Context>/<Provider>/`, with its own history table.
`tools/ef/Elsa.EntityFrameworkCore.Tooling` is the shared design-time startup project: it holds the provider
engines and one `ModuleDesignTimeFactory` line per provider-derived context. Add a line there when a module
gains a context. `module-migrate.sh` also builds this same project as the `--host` it points the
`dotnet elsa persistence` CLI at (#1878) — every first-party module and every provider engine, in one place.

```bash
bash tools/ef/generate-module-migrations.sh [context-regex]   # regenerate Initial after a model change
bash tools/ef/module-migrate.sh pending                        # CI-safe: fails on an unmigrated model change
bash tools/ef/module-migrate.sh apply PostgreSql --connection-env ELSA_EF_CONNECTION # out-of-process apply, every module
bash tools/ef/module-migrate.sh validate PostgreSql --connection-env ELSA_EF_CONNECTION
bash tools/ef/module-migrate.sh script PostgreSql db/migrations  # reviewable SQL, no database needed
bash tools/ef/module-migrate.sh script-check db/migrations       # CI-safe: fails on edited/stale SQL
```

`apply`, `validate`, `script` and `script-check` are thin shims (#1878) over the `dotnet elsa persistence`
CLI (`src/Elsa/Cli`, [spec 171](../../specs/171-persistence-script-cli/spec.md)) rather than a second
implementation of the same behavior; they no longer drive `dotnet ef` themselves. `pending` alone still does,
because it needs no host closure, only this tooling project's own compiled model. `[modules]` selects by the
CLI's own canonical module names (`dotnet elsa persistence list`), comma-separated — not the context-name
regex this script used to take — and defaults to every module.

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

**Layout: flat, ordered `<output-dir>/NN-<slug>.sql` files plus one `migration-plan.json`** (#1878) — the
CLI's own artifact, not the `<output-dir>/<Module>/<Provider>.sql` tree this script wrote before it became a
shim: a DBA pipeline applies files in the order they are named, and the manifest is what `script-check` reads
back rather than a directory listing. See the CLI's own [README](../../src/Elsa/Cli/README.md) for the exact
shape.

Every file is generated idempotent, which means:

- It is **safe to re-run**: each migration in it is wrapped in a check against that module's own
  migrations-history table, so a migration already recorded there is skipped rather than re-applied.
- It **records what it applied** into that same per-module `__EFMigrationsHistory_*` table — Runtime's is
  `__EFMigrationsHistory_ElsaRuntime`, Activities Design's is `__EFMigrationsHistory_ElsaActivitiesDesign`,
  and each script names its own in its first statement. That is the table a host started with
  `Elsa:Persistence:EntityFramework:Migrate:Policy=Validate` reads when it decides whether the database is
  up to date, so applying the script and starting the host in `Validate` agree by construction.
- It needs **no database to generate**: the design-time factories bind a placeholder connection unless
  `ELSA_EF_CONNECTION` names a real one, and scripting never opens it.

**SQLite is refused, not scripted.** EF cannot generate an idempotent script for SQLite
(`SqliteHistoryRepository.GetEndIfScript` throws `NotSupportedException`, because SQLite has no
conditional statement to wrap a migration in), and a plain script would sit in the same tree looking like
every other file while being unsafe to re-run. `script Sqlite` therefore exits 2 and says so. A SQLite
database is brought up to date with `module-migrate.sh apply Sqlite --connection-env ELSA_EF_CONNECTION`, or by a host on the
`AutoMigrate` default.

### Keeping the committed SQL honest

`script-check` regenerates the committed artifact from its own `migration-plan.json` and diffs against it —
no `<Provider>` or module selector of its own, because the committed plan already says what it targets:

```bash
bash tools/ef/module-migrate.sh script-check db/migrations
```

A CI job would call exactly that, once per committed artifact directory, after `dotnet tool restore` —
nothing else is wired up here. It catches the two silent failures that matter: SQL hand-edited after review,
and a model change merged without a regenerated script. The second one is caught because the command builds
the tooling project — and with it every module — before it checks anything, so it compares against the
current model, not a stale assembly.

### Where the SQL is committed

This repository does not commit generated `.sql`. A team that reviews SQL commits the directory `script`
writes — conventionally `db/migrations/<provider>/` — because that is what makes the change reviewable: the
schema diff shows up in the pull request next to the model change that caused it, a DBA approves the
statements before anything runs, and `script-check` in CI proves the committed files are still what the
model generates.

## Layout

- **Module package** (`src/Elsa/<Domain>/Persistence/EntityFrameworkCore/`): `*DbContext` + derived
  provider contexts + `Migrations/<Provider>/`. References EF Core + Relational only. This is the
  assembly Nuplane loads at apply time.
- **Design-time startup project** (`tools/ef/Elsa.EntityFrameworkCore.Tooling/`): one
  `ModuleDesignTimeFactory` line per provider-derived context, the provider PackageReferences, and
  `Microsoft.EntityFrameworkCore.Design`. One project for every module; #1878 retired the last
  module-owned one (Secrets').
- **Policy package** (`src/Elsa/Persistence/EntityFramework/`): history table name, provider guard,
  AutoMigrate vs Validate. No provider engines.

## Command shape

Always pass `--startup-project` as the shared tooling project and `--project` as the module, so generated
files compile into the assembly Nuplane loads at apply time.

History table: `EfMigrationsHistory.TableName("<Module>")` → `__EFMigrationsHistory_<Module>`.
It comes from the module's own `[EfModule]` declaration, not from a magic base-context property.

## Secrets: the one historical migration chain

Every module keeps a single regenerated `Initial` per provider except Secrets, which keeps its historical
chain on Sqlite, SqlServer and PostgreSql (its MySQL set is regenerated with everything else). A model
change there adds a named migration to each of those three chains:

```bash
dotnet tool restore   # restores the pinned dotnet-ef 10.0.10 from .config/dotnet-tools.json
# or: dotnet tool install dotnet-ef --version 10.0.10 --tool-path .tools

bash tools/ef/generate-ef-migrations.sh <MigrationName>
```

That script is the only thing about Secrets that is special. It uses the same shared startup project and
the same provider-free module assembly as every other module: #1878 retired the Secrets-only `Tooling/`
project, and `dual-migrate.sh` with it.

The helper resolves EF tooling in this order: an executable repository-local `.tools/dotnet-ef`,
the repository-manifest `dotnet ef` after `dotnet tool restore`, and only then a `dotnet-ef`
executable on `PATH`. This prevents a stale global tool from silently overriding the repository's
pinned version. The explicit `--tool-path .tools` installation is useful for a clean operator
checkout; the manifest path is the normal CI path.

## Applying out of process

Applying, validating and repairing any module — Secrets included — goes through the same commands:

```bash
bash tools/ef/module-migrate.sh pending 'Secrets.*'
bash tools/ef/module-migrate.sh apply PostgreSql --connection-env ELSA_EF_CONNECTION Secrets
bash tools/ef/module-migrate.sh validate PostgreSql --connection-env ELSA_EF_CONNECTION Secrets
dotnet elsa persistence post-migrate --host <host> --provider PostgreSql --modules Secrets \
  --connection-env ELSA_EF_CONNECTION
```

The connection is never an argument: `--connection-env NAME` reads it from the environment and
`--connection-stdin` reads it from stdin. `post-migrate` has no shim command of its own; run the CLI
against the host directly, which is what an operator with a packaged host does for all of these.

## What the checks mean

The checks cover different failure classes:

- `pending` runs `migrations has-pending-model-changes` for every matched module context. It compares the
  current EF model with that context's committed model snapshot and does not inspect database
  history. A failure means a migration is missing from source; it is not proof that a database has
  unapplied migrations.
- `apply` applies the compiled migrations missing from each selected module's
  `__EFMigrationsHistory_*` table, then audits that module's declared post-migration actions and fails
  closed — naming `dotnet elsa persistence post-migrate` — when one is required. The migrations are
  applied either way; what the refusal withholds is the claim that the database is ready.
- `post-migrate` is the only command that runs a post-migration action. For Secrets that is the
  projection reindex: it targets fields written by the pre-contract host-runtime casing algorithm,
  derives them from the stored document in bounded transactions, preserves concurrency tokens, and is
  idempotent.
- Runtime `Elsa:Persistence:EntityFramework:Migrate:Policy=Validate` calls EF's
  pending-database-migration check and fails closed when the database history is behind. Both
  runtime policies then audit the module's declared post-migration actions — for Secrets, the pinned
  projection contract — and fail closed naming `dotnet elsa persistence post-migrate` when a repair
  is still required. Neither runtime path rewrites legacy rows, and neither replaces the
  source/model `pending` check. Since #1877 that audit is the general
  `IEfPostMigrationAction` seam rather than a Secrets-specific call.

## Deployment and rollback boundary

Name the provider and the modules explicitly, and point `--connection-env` at the variable carrying the
target database's connection string. Before applying to a deployment database, take a backup, quiesce
writes, and run `pending`. Use a short-lived least-privilege deployment identity with the DDL rights
needed for that provider; after the schema is verified, run the application with its least-privilege
runtime identity.

After `apply` succeeds, verify the selected database's per-module `__EFMigrationsHistory_*` tables
contain the expected migration IDs and that the expected table shapes are present before deploying the
application with `Elsa:Persistence:EntityFramework:Migrate:Policy=Validate`. If `apply` reports a required
post-migration action, run it and re-run `validate`; a host under `Validate` refuses to start until it is
clear. If `pending` or `apply` fails, stop the rollout and inspect both the provider schema and history
before retrying; a failed command is not deployment proof.

Prefer additive/expand-contract schema changes so an application binary can be rolled back only
after compatibility is checked. Do not blindly run a down-migration as an application rollback:
Secrets' `WidenLookupKeys.Down` narrows SQL Server/PostgreSQL lookup columns back to 64 characters and can
fail or lose values longer than that. Use a verified backup restore or a forward migration for
schema recovery; treat application rollback and schema recovery as separate decisions.

Runtime (in-process) apply after CShells enable or reload uses the same `EfMigratePolicy` /
`EfDatabaseMigrator` path as a plain host, through the shared `EfModuleMigrator<TContext>`. That is
not a substitute for this out-of-process tool.

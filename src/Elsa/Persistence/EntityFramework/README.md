# Elsa.Persistence.EntityFramework

Shared **policy** surface for first-party EF Core persistence: history-table naming,
provider guard, and migrate/validate apply modes. It began as the ADR 0072 Secrets pilot
and now serves the accepted ADR 0073 migration program.

It is **not** a second storage library, not a mandatory Elsa `DbContext` base, and not a place to
accumulate entity configuration. Module-owned derived contexts remain first-class
(framework §2.9).

## Status

Accepted [ADR 0073](../../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md)
makes EF Core the only first-party persistence implementation family. It supersedes ADR 0042's
single-storage-library policy, ADR 0065's proposed target/lane topology, and ADR 0072's bounded
EF lane while preserving each record's distinct history. The Secrets implementation remains opt-in
until its migration slice proves four-provider parity and performs the explicit default flip.

## What this package owns

| Helper | Role |
|---|---|
| `EfMigrationsHistory.TableName(module)` | `__EFMigrationsHistory_<Module>` so two modules in one database do not share history |
| `EfProviderGuard.Ensure` | Refuse apply when `Database.ProviderName` does not match the derived context |
| `EfMigratePolicy` | `AutoMigrate` vs `Validate` (fail if pending), chosen by the operator through `EfMigrateOptions` |
| `EfDatabaseMigrator.ApplyAsync` | Guard, then `MigrateAsync` (EF 9+ takes the database lock) or fail closed |
| `EfRelationalProviderBinding` | Invoke host-supplied `UseSqlite` / `UseSqlServer` / `UseNpgsql` / `UseMySQL` without this package referencing those engines; `Select` matches a provider name to what a module registers for that dialect |
| `EfSchema` | Resolve and validate the optional database schema a module's tables and history table live in |
| `EfSchemaMigrationsAssembly` | Put migrations scaffolded without a schema into the configured one, so one migration set applies anywhere |
| `EfConnectionDefaults.ResolveConnectionString` | Explicit connection string, then a named `ConnectionStrings` entry (refused when missing or blank), then the module's default entry, then the SQLite file |
| `EfModuleBinding` | A module's owner name, history table, migrations assembly and connection defaults; selects its per-dialect registration and binds its context |
| `EfSharedTransaction` | Own one connection and one transaction for several module contexts that must commit together; refuse split targets and provider mismatches |
| `EfRelationalExceptionClassifier` | Classify unique-key violations and transient write conflicts by provider error code without referencing provider engines; `IsSaveConflict` recognizes a race SaveChanges reported even when the provider's execution strategy wrapped it in `InvalidOperationException` (SQL Server, PostgreSQL) |
| `EfProviderBindingValidator` | Fail a host closed at startup, in the CShells `Prepare` phase ahead of every module migrator, when a configured module's provider engine is missing or no longer exposes what the reflection binding calls |
| `EfWriteRetry` | The one bounded retry loop for compare-and-swap and race-prone store writes: the store supplies budget (`DefaultMaxAttempts` unless pinned), the `EfWriteConflict` kinds or predicate it retries, backoff, and exhaustion outcome; a transient conflict inside a caller's open transaction is rethrown, never retried. See [Retry policy](#retry-policy) |
| `UnicodeOrdinalCasingTable` | The pinned Unicode simple-uppercase mappings that Secrets and OpenTelemetry project persisted ordinal-ignore-case search keys from; each consumer pins `ComputeMappingFingerprint()` in its algorithm id, so the table is never edited in place |

## Retry policy

Accepted [ADR 0074](../../../../docs/adr/0074-first-party-ef-stores-retry-in-bounded-application-loops.md):
**first-party EF stores retry in bounded application loops, and no context enables EF's retrying
execution strategy.** `EnableRetryOnFailure` and `Database.CreateExecutionStrategy` appear nowhere in
this repository, and that is deliberate, not an oversight. The reasons are recorded in the ADR;
the short form is that nine of the thirteen Elsa `DbContext` types own transactions a retrying
strategy forbids, SQLite has no retrying strategy to enable and is the provider the container-free
suites use, and `EfRelationalProviderBinding` has no per-provider seam for the option. The ADR names the
conditions under which that is revisited.

Three rules apply when you write or review a store:

1. **Retry through `EfWriteRetry`, not a hand-rolled loop.** Declare the budget, the
   `EfWriteConflict` kinds or the predicate, any backoff, and what exhaustion means for the store's
   contract. `EfWriteRetry` refuses to retry a transient conflict while a transaction the caller owns
   is still open on the context, because the provider may already have rolled it back; the caller
   retries its whole unit instead.

2. **Classify with `IsSaveConflict`, which walks the exception chain.** Do not match on the type of
   the outermost exception. The *non-retrying* default execution strategies of SQL Server and
   PostgreSQL raise a transient error, deadlock included, as an `InvalidOperationException` wrapping
   the `DbUpdateException`, and a store's own persistence boundary may wrap that again. A store that
   keys on the outer type looks correct on SQLite, where nothing wraps, and silently stops retrying
   on the two providers where it matters. This is what #1801 and #1816 had to retrofit into ten
   stores; write it in from the start.

3. **Include `Transient` deliberately.** A predicate of `Concurrency | UniqueKey` alone means a
   deadlock or lock timeout surfaces to the caller. That is a legitimate choice for some contracts,
   but make it a choice: it is the difference between a lost race, which is the caller's to resolve,
   and a provider conflict, which is not.

## Putting Elsa in its own schema (operator setting)

An Elsa deployment that shares a database with an application which owns the default schema puts every
module's tables, and every module's own `__EFMigrationsHistory_<Module>` table, in a schema of its own:

```jsonc
// appsettings.Production.json
{
  "Elsa": { "Persistence": { "EntityFramework": { "Schema": "elsa" } } }
}
```

```bash
export Elsa__Persistence__EntityFramework__Schema=elsa
```

A module that splits onto its own database can override it on its own feature (`Schema`), which wins over
the host-wide key. Unset means what every deployment has today: the provider's own default schema.

- **SQL Server and PostgreSQL** apply it, and create it: on both, EF's own migrations-history script creates
  the schema before it creates `__EFMigrationsHistory_<Module>` inside it, which is the first thing a migrate
  does. Nothing has to exist beforehand but the database.
- **SQLite** ignores it. SQLite has no schemas, and a qualified name there addresses an attached database
  file. One appsettings file can therefore name a schema and still run the SQLite developer shell.
- **MySQL refuses it.** A MySQL schema *is* a database, so the setting would mean something different there
  from what it means everywhere else, and `MySql.EntityFrameworkCore`'s history repository writes
  `CREATE DATABASE IF NOT EXISTS …` with no statement terminator in front of its `CREATE TABLE`, which the
  server rejects on the first migration. A MySQL host names the database in its connection string instead
  (`Database=elsa`), which says the same thing in MySQL's own terms; the error says so.

The value must be a plain identifier — ASCII letters, digits, `_` or `$`, up to 64 characters. A name that
would need escaping is refused rather than escaped, because it is a configuration mistake rather than a schema.

Migrations are deliberately scaffolded against a schema-less context, so one migration set applies into
whatever schema a host picks: `EfSchemaMigrationsAssembly` fills the schema into every operation as the
migration is read, because EF's SQL generator reads a schema off each operation and `HasDefaultSchema` never
reaches migration DDL. Two consequences are worth knowing:

- A migration that runs **raw SQL** cannot be redirected, so a configured schema refuses it by name rather
  than applying it to the wrong schema.
- The model snapshot a migration carries differs from a schema-configured model by exactly that default
  schema, which EF reads as un-migrated model drift. A schema-configured context therefore suppresses
  `RelationalEventId.PendingModelChangesWarning`. Real drift is still caught where the schema is not in
  play: the model-versus-migrations test in CI, and `tools/ef/module-migrate.sh pending`.

Applying migrations **out of process** into a schema works through the same path: set
`ELSA_EF_SCHEMA` beside `ELSA_EF_CONNECTION` for `tools/ef/module-migrate.sh`. Scripting
(`script` / `script-check`) honours it too, so the artifact a DBA reviews is the schema-qualified one.
Never set it while *generating* migrations; `tools/ef/generate-module-migrations.sh` clears it for that reason.

## Pooled contexts (operator setting)

Every module feature also takes `Pooling`, which registers its context through `AddDbContextPool` instead of
`AddDbContext`, so instances are reused across scopes rather than constructed per scope. It is off by default.

Pooling is safe on **every** first-party module context, and that is a property of how they are written
rather than a case-by-case judgement: each one is constructed from its `DbContextOptions<TContext>` alone and
keeps no instance state, so there is nothing for a reused instance to carry from one request into the next.
Per-request state in EF persistence lives in the stores and commands that take a context — `IPersistenceAccessContextAccessor`,
persistence scopes — and those stay scoped whether or not the context is pooled.
`ModuleSchemaTests.Every_module_context_is_constructed_from_its_options_alone` keeps that true: a module
context that starts taking a service, or holding a field, fails it rather than quietly corrupting under pooling.

The Runtime module's features share one context, so schema and pooling join the provider and connection in
what its participants must agree on; a second feature that asks for a different one is refused at composition
rather than silently ignored.

## Choosing the policy (operator setting)

`EfMigrateOptions.Policy` is bound from configuration, so switching a deployment to out-of-process
migration needs no code change. Every module that registers a migrator binds the same key once:

```jsonc
// appsettings.Production.json
{
  "Elsa": { "Persistence": { "EntityFramework": { "Migrate": { "Policy": "Validate" } } } }
}
```

```bash
export Elsa__Persistence__EntityFramework__Migrate__Policy=Validate
```

- Unset (or blank) means `AutoMigrate`, which is the default a developer host runs on.
- A value that is neither `AutoMigrate` nor `Validate` fails the host with a message naming the key;
  it is never quietly treated as the default, because that would auto-migrate the database the
  operator meant to protect.
- In a CShells host the key is read from the shell's configuration, which layers the shell's own
  `Configuration` node over the host's, so one shell can run `Validate` while another does not.
- A host that calls `services.Configure<EfMigrateOptions>(…)` after composing the module still wins:
  `IConfigureOptions` run in registration order.

Under `Validate` a database that is behind fails `EfDatabaseMigrator.ApplyAsync`, which in a shell
fails activation:

```
RuntimeSqliteDbContext has pending migrations: 20260911000000_Initial. Apply them out of
process (tools/ef/module-migrate.sh) or set Elsa:Persistence:EntityFramework:Migrate:Policy to AutoMigrate.
```

## Custom migrate loops

`Database.MigrateAsync` already acquires `IHistoryRepository.AcquireDatabaseLockAsync`.
Hosts that call `IMigrator.Migrate` or apply pending migrations themselves **must take the
same lock** or concurrent hosts race. Do not roll a second lock around `MigrateAsync`.

## Dual apply

`EfDatabaseMigrator.ApplyAsync` is the in-process path (feature enable and CShells reload).
Out-of-process apply and fail-if-pending for the current Secrets implementation live in
[tools/ef/dual-migrate.sh](../../../../tools/ef/dual-migrate.sh) (`dotnet ef database update`
and `dotnet ef migrations has-pending-model-changes` per derived context).

## Provider packages stay in the host

This project has no `PackageReference` to SqlServer, Sqlite, Npgsql, or MySql.EntityFrameworkCore. The host (or a
design-time tooling project) brings exactly one runtime provider package.

Because the binding is reflective, a missing or mismatched provider package cannot fail at compile time. Two
guards replace that compile check:

- **At startup.** `AddEfModuleMigrations<TContext>` records the provider each module context is configured for, and
  `EfProviderBindingValidator` probes every one of them in the CShells `Prepare` phase — before any migrator runs, and
  as the first hosted service on a plain host. The error names each module, its configured provider, the missing
  assembly or extension method, and the `PackageReference` to add.
- **In CI.** [tests/Elsa/Persistence/EntityFramework/BindingDriftTests](../../../../tests/Elsa/Persistence/EntityFramework/BindingDriftTests)
  references all four engines at the versions `Directory.Packages.props` pins and binds against each, so a provider
  upgrade that renames, moves, or reshapes a `Use*` extension fails CI rather than a host.

### Why no typed registration helper

ADR 0073's [EF dependency guard](../../../../tests/Elsa/Architecture/EfCoreDependencyGuardTests.cs) admits engine
packages in `Elsa.Workbench` only, and asserts each admitted module project's EF closure exactly. Adding a typed
`UseSqlServer` call to a module project would therefore fail the guard as an unexpectedly resolved package, and
splitting each module into four provider-specific projects would turn thirteen module projects into fifty-two and
needs its own ADR. Reflection stays the binding; the two guards above cover what a typed call would have caught.

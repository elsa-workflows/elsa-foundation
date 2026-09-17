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
| `EfConnectionDefaults.ResolveConnectionString` | Explicit connection string, then a named `ConnectionStrings` entry (refused when missing or blank), then the module's default entry, then the SQLite file |
| `EfModuleBinding` | A module's owner name, history table, migrations assembly and connection defaults; selects its per-dialect registration and binds its context |
| `EfSharedTransaction` | Own one connection and one transaction for several module contexts that must commit together; refuse split targets and provider mismatches |
| `EfRelationalExceptionClassifier` | Classify unique-key violations and transient write conflicts by provider error code without referencing provider engines; `IsSaveConflict` recognizes a race SaveChanges reported even when the provider's execution strategy wrapped it in `InvalidOperationException` (SQL Server, PostgreSQL) |
| `EfProviderBindingValidator` | Fail a host closed at startup, in the CShells `Prepare` phase ahead of every module migrator, when a configured module's provider engine is missing or no longer exposes what the reflection binding calls |
| `EfWriteRetry` | The one bounded retry loop for compare-and-swap and race-prone store writes: the store supplies budget (`DefaultMaxAttempts` unless pinned), the `EfWriteConflict` kinds or predicate it retries, backoff, and exhaustion outcome; a transient conflict inside a caller's open transaction is rethrown, never retried |
| `UnicodeOrdinalCasingTable` | The pinned Unicode simple-uppercase mappings that Secrets and OpenTelemetry project persisted ordinal-ignore-case search keys from; each consumer pins `ComputeMappingFingerprint()` in its algorithm id, so the table is never edited in place |

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
BookmarkStateSqliteDbContext has pending migrations: 20260911000000_Initial. Apply them out of
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

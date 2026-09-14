# Elsa.Persistence.EntityFramework

Shared **policy** surface for first-party EF Core persistence: history-table naming,
provider guard, and migrate/validate apply modes. It began as the ADR 0072 Secrets pilot
and now serves the accepted ADR 0073 migration program.

It is **not** a second Groundwork, not a mandatory Elsa `DbContext` base, and not a place to
accumulate entity configuration. Module-owned derived contexts remain first-class
(framework §2.9).

## Status

Accepted [ADR 0073](../../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md)
makes EF Core the only first-party persistence implementation family. It supersedes ADR 0042's
Groundwork-only policy, ADR 0065's proposed Groundwork target/lane topology, and ADR 0072's bounded
EF lane while preserving each record's distinct history. The Secrets implementation remains opt-in
until its migration slice proves four-provider parity and performs the explicit default flip.

## What this package owns

| Helper | Role |
|---|---|
| `EfMigrationsHistory.TableName(module)` | `__EFMigrationsHistory_<Module>` so two modules in one database do not share history |
| `EfProviderGuard.Ensure` | Refuse apply when `Database.ProviderName` does not match the derived context |
| `EfMigratePolicy` | `AutoMigrate` vs `Validate` (fail if pending) |
| `EfDatabaseMigrator.ApplyAsync` | Guard, then `MigrateAsync` (EF 9+ takes the database lock) or fail closed |
| `EfRelationalProviderBinding` | Invoke host-supplied `UseSqlite` / `UseSqlServer` / `UseNpgsql` / `UseMySQL` without this package referencing those engines |

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

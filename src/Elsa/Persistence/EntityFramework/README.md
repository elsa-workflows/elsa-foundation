# Elsa.Persistence.EntityFramework

Shared **policy** surface for first-party EF Core persistence. This is the ADR 0072 Secrets
pilot package: history-table naming, provider guard, and migrate/validate apply modes.

It is **not** a second Groundwork, not a mandatory Elsa `DbContext` base, and not a place to
accumulate entity configuration. Module-owned derived contexts remain first-class
(framework §2.9).

## Status

This package is an intentional first-party EF edge under **proposed**
[ADR 0072](https://github.com/elsa-workflows/elsa-foundation/pull/1623).
[ADR 0042](../../../../docs/adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md)
still forbids first-party EF except the OpenIddict vendor exception and this reviewed
pilot allowlist. Accepting 0072 formally narrows 0042.

## What this package owns

| Helper | Role |
|---|---|
| `EfMigrationsHistory.TableName(module)` | `__EFMigrationsHistory_<Module>` so two modules in one database do not share history |
| `EfProviderGuard.Ensure` | Refuse apply when `Database.ProviderName` does not match the derived context |
| `EfMigratePolicy` | `AutoMigrate` vs `Validate` (fail if pending) |
| `EfDatabaseMigrator.ApplyAsync` | Guard, then `MigrateAsync` (EF 9+ takes the database lock) or fail closed |
| `EfRelationalProviderBinding` | Invoke host-supplied `UseSqlite` / `UseSqlServer` / `UseNpgsql` without this package referencing those engines |

## Custom migrate loops

`Database.MigrateAsync` already acquires `IHistoryRepository.AcquireDatabaseLockAsync`.
Hosts that call `IMigrator.Migrate` or apply pending migrations themselves **must take the
same lock** or concurrent hosts race. Do not roll a second lock around `MigrateAsync`.

## Provider packages stay in the host

This project has no `PackageReference` to SqlServer, Sqlite, or Npgsql. The host (or a
design-time tooling project) brings exactly one runtime provider package.

# Extension points — Secrets.Persistence.EntityFrameworkCore

EF Core implementation of `ISecretRepository`, per accepted
[ADR 0073](../../../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md).
Workbench catalogs this feature and the default shells enable it.

Proof is `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/` plus the `Secrets EF composition`
CI job on a native PostgreSQL, and the SQL Server and MySQL legs of the EF container-suite matrix.

## Replacement contract

- **`ISecretRepository`** (and `IRevisionAwareSecretRepository` / `IPagedSecretRepository` via the same instance): `EfSecretRepository` when `SecretsEntityFrameworkCore` is enabled. Registration records an EF-local `SecretRepositoryBackend` name and refuses any other prior backend — order-independent, not silent last-write-wins.

## Shell feature

- **`SecretsEntityFrameworkCoreFeature`**: host-selected provider (`Sqlite` / `SqlServer` / `PostgreSql`), connection string or named connection, and `EfMigratePolicy`. The host must reference the matching EF provider engine; this package does not.

## Lifecycle

- **`SecretsEfMigrationHostedService`**: one instance registered as `IHostedService` (plain hosts / tests) and CShells `IShellInitializer` (enable and reload). It calls `EfDatabaseMigrator.ApplyAsync` with the feature's `EfMigratePolicy`.

## Derived contexts

`SecretsDbContext` is shared model configuration. `SecretsSqliteDbContext`,
`SecretsSqlServerDbContext`, and `SecretsPostgreSqlDbContext` each own a migrations set.
They are not a mandated application `DbContext` base (framework §2.9).

## Gate ownership

This module's tests and the `Secrets EF composition` CI job own the Secrets persistence proof.

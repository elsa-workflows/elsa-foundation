# Extension points — Secrets.Persistence.EntityFrameworkCore

Additive EF Core replacement for `ISecretRepository`. Groundwork remains the default
first-party store.

## Replacement contract

- **`ISecretRepository`** (and `IRevisionAwareSecretRepository` / `IPagedSecretRepository` via the same instance): `EfSecretRepository` when `SecretsEntityFrameworkCore` is enabled. Registration records `SecretRepositoryBackend.EntityFramework` and refuses a prior Groundwork (or other) backend — order-independent, not silent last-write-wins.

## Shell feature

- **`SecretsEntityFrameworkCoreFeature`**: host-selected provider (`Sqlite` / `SqlServer` / `PostgreSql`), connection string or named connection, and `EfMigratePolicy`. The host must reference the matching EF provider engine; this package does not.

## Lifecycle

- **`SecretsEfMigrationHostedService`**: one instance registered as `IHostedService` (plain hosts / tests) and CShells `IShellInitializer` (enable and reload). It calls `EfDatabaseMigrator.ApplyAsync` with the feature's `EfMigratePolicy`.

## Derived contexts

`SecretsDbContext` is shared model configuration. `SecretsSqliteDbContext`,
`SecretsSqlServerDbContext`, and `SecretsPostgreSqlDbContext` each own a migrations set.
They are not a mandated application `DbContext` base (framework §2.9).

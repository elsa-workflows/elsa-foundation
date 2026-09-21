# Extension points — Secrets.Persistence.EntityFrameworkCore

EF Core implementation of `ISecretRepository`, per accepted
[ADR 0073](../../../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md).
Workbench catalogs this feature and the default shells enable it.

Proof is `tests/essentials/Secrets/Persistence/EntityFrameworkCore/` plus the `Secrets EF composition`
CI job on a native PostgreSQL, and the SQL Server and MySQL legs of the EF container-suite matrix.

## Replacement contract

- **`ISecretRepository`** (and `IRevisionAwareSecretRepository` / `IPagedSecretRepository` via the same instance): `EfSecretRepository` when `SecretsEntityFrameworkCore` is enabled. Registration records an EF-local `SecretRepositoryBackend` name and refuses any other prior backend — order-independent, not silent last-write-wins.

## Shell feature

- **`SecretsEntityFrameworkCoreFeature`**: host-selected provider (`Sqlite` / `SqlServer` / `PostgreSql` / `MySql`), connection string or named connection, schema and pooling. The host must reference the matching EF provider engine; this package does not. The migrate policy is **not** a setting here: it is the host-wide `Elsa:Persistence:EntityFramework:Migrate:Policy`. The obsolete `MigratePolicy` property remains for one release only so a still-configured value is refused rather than silently ignored — a shell that sets it fails to start (ADR 0076 D8, FR-058).

## Lifecycle

- **`EfModuleMigrator<SecretsDbContext>`**: the shared migrator every EF module registers through `AddEfModuleMigrations<TContext>`, one instance registered as `IHostedService` (plain hosts / tests) and CShells `IShellInitializer` (enable and reload, `LifecyclePhase.Prepare`). It calls `EfDatabaseMigrator.ApplyAsync` with the host-wide `EfMigratePolicy` and then audits this module's declared post-migration actions. The Secrets-specific `SecretsEfMigrationHostedService` it replaces was retired in #1877.

## Post-migration action

- **`SecretsProjectionReindex`** (`IEfPostMigrationAction`): declared on this assembly's `[EfModule(... PostMigration = ...)]`. Its audit is read-only and runs after every apply under both migrate policies; a database with legacy projection rows fails closed naming `dotnet elsa persistence post-migrate --modules Secrets --provider <provider>`. Nothing ever runs the reindex automatically — `post-migrate` is the only caller of `RunAsync`.

## Derived contexts

`SecretsDbContext` is shared model configuration. `SecretsSqliteDbContext`,
`SecretsSqlServerDbContext`, `SecretsPostgreSqlDbContext`, and `SecretsMySqlDbContext` each own a
migrations set.
They are not a mandated application `DbContext` base (framework §2.9).

## Gate ownership

This module's tests and the `Secrets EF composition` CI job own the Secrets persistence proof.

# Extension points — Secrets.Persistence.EntityFrameworkCore

EF Core replacement for `ISecretRepository` and the first existing implementation in the accepted
[ADR 0073](../../../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md)
migration program. Groundwork remains the temporary default until the Secrets four-provider gate and
explicit flip complete. Workbench catalogs this feature; default shells do not yet enable it.

Phase 4 (#1631): Groundwork Secrets matrix and ledger growth are **not** prerequisites for this
feature. EF-selected proof is `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/` plus the
`Secrets EF composition` CI job. Groundwork-selected proof stays in
`tests/Elsa/Secrets/Persistence/Groundwork/` and the Groundwork v2 native provider matrix job.

## Replacement contract

- **`ISecretRepository`** (and `IRevisionAwareSecretRepository` / `IPagedSecretRepository` via the same instance): `EfSecretRepository` when `SecretsEntityFrameworkCore` is enabled. Registration records an EF-local `SecretRepositoryBackend` name and refuses a prior Groundwork (or other) backend — order-independent, not silent last-write-wins.

## Shell feature

- **`SecretsEntityFrameworkCoreFeature`**: host-selected provider (`Sqlite` / `SqlServer` / `PostgreSql`), connection string or named connection, and `EfMigratePolicy`. The host must reference the matching EF provider engine; this package does not. Enable this feature **or** `SecretsGroundworkPersistence`, never both.

## Lifecycle

- **`SecretsEfMigrationHostedService`**: one instance registered as `IHostedService` (plain hosts / tests) and CShells `IShellInitializer` (enable and reload). It calls `EfDatabaseMigrator.ApplyAsync` with the feature's `EfMigratePolicy`.

## Derived contexts

`SecretsDbContext` is shared model configuration. `SecretsSqliteDbContext`,
`SecretsSqlServerDbContext`, and `SecretsPostgreSqlDbContext` each own a migrations set.
They are not a mandated application `DbContext` base (framework §2.9).

## Gate ownership

Groundwork Secrets matrix/ledger growth is owned by `SecretsGroundworkPersistence` (#1631). This
module's tests and the `Secrets EF composition` CI job are the EF-selected proof. Do not treat
four-provider Groundwork Secrets evidence as a prerequisite for enabling this feature.

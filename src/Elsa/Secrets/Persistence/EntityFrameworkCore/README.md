# Elsa.Secrets.Persistence.EntityFrameworkCore

Additive, opt-in EF Core persistence for Secrets. **Groundwork remains the default** durable
store. Workbench catalogs this feature so a host can enable it; default `shells.json` files
keep `SecretsGroundworkPersistence` and do not enable this feature.

## Proposed ADR 0072 / ADR 0042

This is an intentional first-party EF pilot under **proposed**
[ADR 0072](https://github.com/elsa-workflows/elsa-foundation/pull/1623) (provider-derived
`DbContext` types, Variant A). [ADR 0042](../../../../../docs/adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md)
still forbids first-party EF except the OpenIddict vendor exception. The ratchet may
exclude these proposed pilot paths for review; that exclusion is not an accepted ADR
amendment. Accepting 0072 formally narrows 0042. This PR does not accept 0072.

The module package references `Microsoft.EntityFrameworkCore` and
`Microsoft.EntityFrameworkCore.Relational` only. Sqlite / SqlServer / Npgsql engines live
in the host or in `Tooling/` (design-time).

## Enable

CShells feature `SecretsEntityFrameworkCore`. The host must reference this module (Workbench
already does, via `WithHostAssemblies()`) and exactly one EF provider package. Workbench
already references `Microsoft.EntityFrameworkCore.Sqlite` for OpenIddict, so Sqlite Secrets
EF works without another provider package. SqlServer / PostgreSql require the host to add
that engine.

**Default stays Groundwork.** In `shells.json` (or the docker compose overlay), replace
`SecretsGroundworkPersistence` with `SecretsEntityFrameworkCore`. Do not leave both keys
in the same shell — registration throws an `InvalidOperationException` naming both backends.

```json
{
  "CShells": {
    "Shells": {
      "default": {
        "Features": {
          "Secrets": {},
          "SecretsApi": {},
          "SecretsEntityFrameworkCore": {
            "Provider": "Sqlite",
            "ConnectionString": "Data Source=elsa-secrets.db",
            "MigratePolicy": "AutoMigrate"
          }
        }
      }
    }
  }
}
```

A Nuplane / Foundation Host feed composition is the same swap: load this package, enable
the feature, omit `SecretsGroundworkPersistence`.

`ConnectionName` looks up `ConnectionStrings:<name>` when `ConnectionString` is omitted.
`MigratePolicy` is `AutoMigrate` (default) or `Validate` (fail if pending). Both policies
run when the feature is enabled **and** when CShells reloads the shell (`IShellInitializer`).
A plain host uses the same instance as `IHostedService`.

## Schema

Table `elsa_secrets`. Key: `TenantId` + `NormalizedName`. Projected list/search columns
match Groundwork `SecretsGroundworkStorageSchema`. Full `Secret` document in `Payload`.
OCC is an explicit `ConcurrencyToken` stamped on save — **not** cross-provider
`IsRowVersion()` (Sqlite inserts NULL). On Sqlite, `MaxActiveVersionExpiresAt` is
stored as UTC ticks (`INTEGER`) so active-only expiry comparisons translate.

Derived contexts: `SecretsSqliteDbContext`, `SecretsSqlServerDbContext`,
`SecretsPostgreSqlDbContext`. Each has its own `Migrations/` folder and `ModelSnapshot`.
History table: `__EFMigrationsHistory_ElsaSecrets`.

## Generate and apply migrations

See [Tooling/README.md](Tooling/README.md) and [tools/ef/README.md](../../../../../tools/ef/README.md).

The repository pins `dotnet-ef` in `.config/dotnet-tools.json`; run `dotnet tool restore` from
the repository root. An executable `.tools/dotnet-ef` is an explicit override for an operator
checkout, while a stale global `dotnet-ef` is never preferred over the repository manifest.
The script builds the tooling project once by default. CI or tests that already built the same
checkout and configuration can set `ELSA_SECRETS_EF_SKIP_BUILD=1`; that caller owns artifact
freshness, so operator checkouts should not set it casually.

Out of process (no host):

```bash
bash tools/ef/dual-migrate.sh pending
bash tools/ef/dual-migrate.sh apply --sqlite
```

`pending` is `dotnet ef migrations has-pending-model-changes` per derived context.
`apply` is `dotnet ef database update --context <Derived>`. Runtime AutoMigrate / Validate
uses the same compiled migrations in this module, but the checks are different: `pending`
compares the source model to its snapshot without reading database history, whereas runtime
`Validate` checks whether the selected database has unapplied compiled migrations. Recommended
deployment order is `pending`, backup/quiesce, provider-specific `apply`, history/table-shape
verification, then application startup with `MigratePolicy=Validate`.

For deployment boundaries, use an explicit provider selector and matching connection, a
short-lived least-privilege migration identity, and a least-privilege runtime identity after
verification. Keep `SecretsGroundworkPersistence` disabled in the shell while this backend is
enabled. Verify the `__EFMigrationsHistory_ElsaSecrets` history table and the `elsa_secrets` table
shape before rollout; stop on any failure and inspect the database before retrying. Keep schema changes additive where
possible and roll back application code only after compatibility is checked. Never blindly
down-migrate: `WidenLookupKeys.Down` narrows SQL Server/PostgreSQL lookup columns to 64
characters, so longer values can make rollback fail or lose data. Restore a verified backup or
ship a forward migration for schema recovery.

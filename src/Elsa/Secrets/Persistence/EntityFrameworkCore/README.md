# Elsa.Secrets.Persistence.EntityFrameworkCore

Additive, opt-in EF Core persistence for Secrets. **Groundwork remains the default** durable
store. This package does not switch Workbench and does not remove Groundwork.

## Proposed ADR 0072 / ADR 0042

This is an intentional first-party EF pilot under **proposed**
[ADR 0072](https://github.com/elsa-workflows/elsa-foundation/pull/1623) (provider-derived
`DbContext` types, Variant A). [ADR 0042](../../../../../docs/adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md)
still forbids first-party EF except the OpenIddict vendor exception and this reviewed
allowlist. Accepting 0072 formally narrows 0042. This PR does not accept 0072.

The module package references `Microsoft.EntityFrameworkCore` and
`Microsoft.EntityFrameworkCore.Relational` only. Sqlite / SqlServer / Npgsql engines live
in the host or in `Tooling/` (design-time).

## Enable

CShells feature `SecretsEntityFrameworkCore`. The host must already reference one EF
provider package.

```json
{
  "CShells": {
    "Shells": {
      "default": {
        "Features": {
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

`ConnectionName` looks up `ConnectionStrings:<name>` when `ConnectionString` is omitted.
`MigratePolicy` is `AutoMigrate` (default) or `Validate` (fail if pending).

Do not enable this feature together with `SecretsGroundworkPersistence` in the same shell:
both replace `ISecretRepository`.

## Schema

Table `elsa_secrets`. Key: `TenantId` + `NormalizedName`. Projected list/search columns
match Groundwork `SecretsGroundworkStorageSchema`. Full `Secret` document in `Payload`.
OCC is an explicit `ConcurrencyToken` stamped on save — **not** cross-provider
`IsRowVersion()` (Sqlite inserts NULL).

Derived contexts: `SecretsSqliteDbContext`, `SecretsSqlServerDbContext`,
`SecretsPostgreSqlDbContext`. Each has its own `Migrations/` folder and `ModelSnapshot`.
History table: `__EFMigrationsHistory_ElsaSecrets`.

## Generate migrations

See [Tooling/README.md](Tooling/README.md) and [tools/ef/README.md](../../../../../tools/ef/README.md).

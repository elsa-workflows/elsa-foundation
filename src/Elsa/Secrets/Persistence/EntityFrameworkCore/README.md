# Elsa.Secrets.Persistence.EntityFrameworkCore

EF Core persistence for Secrets, per accepted
[ADR 0073](../../../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md).
Workbench catalogs this feature and the default `shells.json` files enable it.

CI proves it on the independent `Secrets EF composition` job (native PostgreSQL) plus the SQL
Server and MySQL legs of the EF container-suite matrix.

## Governing decision

This implementation began as the provider-derived `DbContext` pilot proposed in ADR 0072.
Accepted ADR 0073 now makes EF Core the only first-party persistence implementation family
and supersedes ADR 0042's opposite direction. The default remains unchanged until the
Secrets migration issue records four-provider, migration-lifecycle, host-composition, and
[#1653 production-shaped HTTP CRUD/restart](https://github.com/elsa-workflows/elsa-foundation/issues/1653)
proof. The [completion ledger](../../../../../docs/reports/ef-core-persistence-completion-ledger.md)
records evidence and dispositions for that gate. ADR 0073 and the active
[EF Core Persistence goal](../../../../../docs/program-goals/ef-core-persistence.md) govern the
decision and program scope; the point-in-time ledger cannot override them.

The module package references `Microsoft.EntityFrameworkCore` and
`Microsoft.EntityFrameworkCore.Relational` only. Sqlite / SqlServer / Npgsql / MySQL engines live
in the host or in `Tooling/` (design-time).

## Enable

CShells feature `SecretsEntityFrameworkCore`. The host must reference this module (Workbench
already does, via `WithHostAssemblies()`) and exactly one EF provider package. Workbench
already references `Microsoft.EntityFrameworkCore.Sqlite` for OpenIddict, so Sqlite Secrets
EF works without another provider package. SqlServer / PostgreSql / MySql require the host to
add that engine.

The MySQL context and provider selection are production-owned, but MySQL migration artifacts and
full lifecycle proof remain deferred to the migration work unit. The focused MySQL model and
repository proof is under `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/MySql/Tests`.

Enable `SecretsEntityFrameworkCore` in `shells.json` (or the docker compose overlay). Registering a
second Secrets persistence backend throws an `InvalidOperationException` naming both backends.

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
            "ConnectionString": "Data Source=elsa-secrets.db"
          }
        }
      }
    }
  }
}
```

Package-feed shells make the same swap: load this package, enable the feature, and omit
`SecretsEntityFrameworkCore`. The package-root `nuplane.json` declares `HostIntegrated`
loading for its dependency closure because the feature contributes shell DI,
hosted/initializer lifecycle services, EF contexts, and migrations. A host override must not
downgrade this package to collectible loading. The current Nuplane-backed Foundation Host route
still requires the unsigned-CShells identity fix tracked by foundation issue #1644; until that
lands, use a host-integrated package-feed composition rather than treating Foundation Host
readiness as proof that the requested feed features activated.

`ConnectionName` looks up `ConnectionStrings:<name>` when `ConnectionString` is omitted.

The migrate policy is the host-wide `Elsa:Persistence:EntityFramework:Migrate:Policy`
(`AutoMigrate` by default, or `Validate` to fail when migrations are pending), the same key every
other EF module reads — see
[the persistence README](../../../Persistence/EntityFramework/README.md#choosing-the-policy-operator-setting).
It applies when the feature is enabled **and** when CShells reloads the shell
(`IShellInitializer`); a plain host uses the same instance as `IHostedService`. A shell that needs a
different policy from its neighbours sets that key under its own `Configuration` node.

> **Behaviour change (#1877).** The Secrets-only `SecretsEntityFrameworkCore:MigratePolicy` setting
> is retired. A shell that still sets it to a non-null value **fails to start**, with a message
> naming the host-wide key; move the value there. The property is kept for one release purely so
> that refusal is possible — CShells' binder never reads a configuration key that has no matching
> property, so deleting it would make a still-configured value silently invisible.

## Schema

Table `elsa_secrets`. Key: `TenantId` + `NormalizedName`. Projected list/search columns
carry the searchable and filterable values. Full `Secret` document in `Payload`.
OCC is an explicit `ConcurrencyToken` stamped on save — **not** cross-provider
`IsRowVersion()` (Sqlite inserts NULL). On Sqlite, `MaxActiveVersionExpiresAt` is
stored as UTC ticks (`INTEGER`) so active-only expiry comparisons translate.

Derived contexts: `SecretsSqliteDbContext`, `SecretsSqlServerDbContext`,
`SecretsPostgreSqlDbContext`, and `SecretsMySqlDbContext`. The SQLite, SQL Server, and PostgreSQL
contexts each have their own `Migrations/` folder and `ModelSnapshot`; MySQL production migrations
are intentionally not included in this slice.
History table: `__EFMigrationsHistory_ElsaSecrets`.

### Persisted text projections

Case-insensitive search and Type/Store/Scope lookup keys use the Elsa-owned
`elsa-secrets-unicode-ordinal-ignore-case-v1-bcbcc4bf0951b182137ed0f42681f30bafda7777f500c42203cf58bb7e4eaaa1`
projection. Its generated table is pinned to Unicode 16 plus the 26 mappings observed on the .NET
10 development host when Phase 1 began writing rows. Phase 1's runtime casing API did not define
portable bytes: .NET can consume different Unicode data on different operating systems. Runtime
casing APIs are no longer used, so changing a host runtime or its Unicode data cannot silently
change newly persisted keys.

Before upgrading a database written by the pre-contract Phase 1 pilot, quiesce writers, apply the
migrations, and then run the declared post-migration action:

```bash
dotnet elsa persistence apply       --modules Secrets --provider <provider>
dotnet elsa persistence post-migrate --modules Secrets --provider <provider>
```

`SecretsProjectionReindex` rewrites the stored projection columns and document copies from the
authoritative `Secret` payload in bounded keyset transactions over `(TenantId, NormalizedName)`
while preserving concurrency tokens. Rows created on a host whose runtime casing data differed from
this pinned table otherwise remain unreadable through canonical lookups. Host startup — and
`apply`/`validate` — audit every row in bounded, read-only keyset pages over
`(TenantId, NormalizedName)` and fail closed naming `post-migrate` instead of silently serving a
partially readable store. No command ever reindexes as a side effect; only `post-migrate` does
(ADR 0076 D8).

The mapping deviates from plain Unicode 16 only at the exact, exhaustively tested boundary `U+017F`
and `U+16EBB` through `U+16ED3`. This module persists projected text rather than a separate
comparison key. Any future projection change requires a new algorithm id, an explicit data
migration/backfill, and compatibility tests; editing v1 in place is forbidden.

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
`apply` runs `dotnet ef database update --context <Derived>` and then the managed projection
reindexer for that same context. Runtime AutoMigrate / Validate uses the same compiled migrations
in this module, but the checks are different: `pending`
compares the source model to its snapshot without reading database history, whereas runtime
`Validate` checks whether the selected database has unapplied compiled migrations; both runtime
policies also reject legacy projection bytes until the operator reindex completes. Recommended
deployment order is `pending`, backup/quiesce, provider-specific `apply`, `dotnet elsa persistence
post-migrate` for any action the apply reports as required, history/table-shape verification, then
application startup with `Elsa:Persistence:EntityFramework:Migrate:Policy=Validate`.

For deployment boundaries, use an explicit provider selector and matching connection, a
short-lived least-privilege migration identity, and a least-privilege runtime identity after
verification. Keep `SecretsEntityFrameworkCore` disabled in the shell while this backend is
enabled. Verify the `__EFMigrationsHistory_ElsaSecrets` history table and the `elsa_secrets` table
shape before rollout; stop on any failure and inspect the database before retrying. Keep schema changes additive where
possible and roll back application code only after compatibility is checked. Never blindly
down-migrate: `WidenLookupKeys.Down` narrows SQL Server/PostgreSQL lookup columns to 64
characters, so longer values can make rollback fail or lose data. Restore a verified backup or
ship a forward migration for schema recovery.

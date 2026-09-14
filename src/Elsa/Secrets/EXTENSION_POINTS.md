# Secrets Extension Points

The `Elsa.Secrets` package provides default services and shell feature registration for named secret management.

## Persistence host selection during the ADR 0073 migration

`Elsa.Secrets.Persistence.Groundwork` is still the default durable replacement for `ISecretRepository`
until the Secrets migration slice completes its explicit flip; it is not the governing first-party direction.
The contracts in this package remain provider-free. Register a Groundwork v2 provider connection and call
`AddGroundworkSecretsStore()`. The feature contributes one scoped, optimistic `StorageUnit` and replaces the
repository as a scoped service. A named target can be supplied when a host routes Secrets to a dedicated store.

An EF Core replacement lives in `Elsa.Secrets.Persistence.EntityFrameworkCore`. It began as the
ADR 0072 pilot and is the first existing implementation feeding accepted
[ADR 0073](../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md). It remains
opt-in while the four-provider replacement gate is incomplete. Workbench catalogs the feature so a
shell can select it; committed default shells temporarily keep
`SecretsGroundworkPersistence` and must not also enable `SecretsEntityFrameworkCore`.
Both registrations record `SecretRepositoryBackend` and throw if the other backend is already selected.

Groundwork Secrets provider-matrix, coverage-ledger growth, and `host-selection-all35` obligations
are owned by the Groundwork-selected composition (#1631). An EF-selected shell does not register the
Groundwork `elsa-secrets` source and is not blocked by those Groundwork-only gates. When this
Groundwork feature is selected or changed, retain
`tests/Elsa/Secrets/Persistence/Groundwork/` (including the native provider matrix). See
[`Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](Persistence/EntityFrameworkCore/EXTENSION_POINTS.md)
and that module's README for the exact feature swap.

## Interim gate ownership — Groundwork vs EF

- **Groundwork-selected** (`SecretsGroundworkPersistence`): `tests/Elsa/Secrets/Persistence/Groundwork/`, ledger row `secrets-repository`, `host-selection-all35`, and the Groundwork v2 native provider matrix CI job.
- **EF-selected** (`SecretsEntityFrameworkCore`): `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/`, `host-selection-ef-secrets-pilot.json` (omits `secrets-repository`), and the independent `Secrets EF composition` CI job.
- Either/or per host. Quarantining Groundwork-only gates must not delete Groundwork Secrets tests.

`SecretsGroundworkStorageSchema` declares the fresh `elsa-secrets` unit. Tenant id and normalized secret name form
its key, searchable/filterable values are projected into typed columns, and the complete secret is retained in a
JSON payload. Every repository operation opens an explicit tenant-scoped v2 session. Reads, writes, counts,
ordering, paging, active-version filtering, and revision preconditions execute through the public Groundwork v2
Store and Query APIs.

Substring search is the one deliberate non-indexed route. It carries the owned, expiring
`GW-SCAN-ELSA-SECRETS-SUBSTRING` acceptance instead of silently falling back to client materialization. There is no
v1 document manifest, legacy tenant backfill, wire-format bridge, or migration path: this integration is a clean
break intended for a fresh store.

## Service Overrides

Use `services.AddSecrets()` to register defaults, then replace these services as needed:

- `ISecretRepository` for durable persistence.
- `ISecretValueProtector` for host-managed encryption keys or external key vault integration.
- `ISecretKeyRing` for custom key-material sourcing (e.g. an external key vault) behind the rotation-aware protector.
- `ISecretAuditSink` for audit export. The default `LoggingSecretAuditSink` emits every audit record and warns once when auditing is unconfigured; register `NullSecretAuditSink` to opt out.
- `ISecretStore` implementations for external providers such as Azure Key Vault, HashiCorp Vault, AWS Secrets Manager, or Kubernetes secrets.
- `ISecretTypeProvider` implementations for domain-specific secret types.

## Master-Key Rotation

The default `ISecretValueProtector` writes a versioned, key-id-tagged payload (`v2:<keyId>:nonce:tag:ciphertext`) and can still read legacy `v1:` payloads via the ring's legacy key. Configure additional keys and switch the active key to rotate without re-encrypting existing data. Key ids are validated at startup (non-empty, no `:`, no duplicates, active key must exist). See [`docs/secrets-key-rotation.md`](../../../docs/secrets-key-rotation.md).

## Built-In Stores

- `encrypted`: writes protected payload material through `ISecretValueProtector`.
- `configuration`: resolves values from `IConfiguration` using a stored configuration key.

## Runtime Integration

The package contributes the `Secret` expression descriptor. Workflow inputs can store a secret expression value such as:

```json
{
  "type": "Secret",
  "value": {
    "name": "payments.api-key",
    "typeName": "text"
  }
}
```

The expression handler resolves the latest active version at execution time and does not persist the resolved value back into workflow state.

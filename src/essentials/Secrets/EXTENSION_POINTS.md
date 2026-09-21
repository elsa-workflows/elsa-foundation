# Secrets Extension Points

The `Elsa.Secrets` package provides default services and shell feature registration for named secret management.

## Persistence

`Elsa.Secrets.Persistence.EntityFrameworkCore` is the durable replacement for `ISecretRepository`, per accepted
[ADR 0073](../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md). The contracts in this
package remain provider-free. Enable `SecretsEntityFrameworkCore` and configure its provider and connection; the
registration records a `SecretRepositoryBackend` and throws if a different backend is already selected, so
selection is order-independent rather than last-write-wins. See
[`Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](Persistence/EntityFrameworkCore/EXTENSION_POINTS.md)
and that module's README for the model, migrations and host settings.

Tenant id and normalized secret name form the row key, searchable and filterable values are projected into typed
columns, and the complete secret is retained in a JSON payload. Every repository operation runs inside an explicit
tenant-scoped persistence access context. Reads, writes, counts, ordering, paging, active-version filtering and
revision preconditions all execute through the module's own EF model.

Substring search is the one deliberate non-indexed route. It carries the owned, expiring
`GW-SCAN-ELSA-SECRETS-SUBSTRING` acceptance instead of silently falling back to client materialization. There is
no legacy tenant backfill, wire-format bridge, or migration path: this integration is a clean break intended for a
fresh store.

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

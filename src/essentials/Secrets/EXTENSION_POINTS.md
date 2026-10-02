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

The package contributes the `Secret` expression descriptor, which the Studio secret picker authors as a reference such
as:

```json
{
  "type": "Secret",
  "value": {
    "name": "payments.api-key",
    "typeName": "text"
  }
}
```

A `Secret` binding is not evaluated as an expression. Publish compiles it into a secret read that carries the reference
and no value, the persisted activity input holds only that reference, and the workflow runtime resolves it each time the
activity is activated, through its own `IRuntimeSecretResolver` contract (spec 188).

The bridge [`Elsa.Secrets.Workflows`](Workflows/README.md) (feature `SecretsWorkflows`) implements that contract over
`ISecretValueResolver`, so replacing `ISecretValueResolver` also changes what workflows resolve:

- The tenant is the one the runtime hands over: the execution partition read at activation, which on the background
  drain path is the host's persistence scope (spec 188 research R2). No binding, setting or default selects it.
- The latest active version is read at every activation and never written back into workflow state, so a rotation takes
  effect at the next activation without republishing.
- A failure faults the activity with the reference name and the failure code only. `StoreUnavailable` is retryable and
  every other code is permanent. `ResolvedSecret.Error` never reaches the fault.
- `DefaultSecretValueResolver` reports `StoreUnavailable`, transient, only for an outage of the payload store
  (`ISecretStore`) or the metadata repository (`ISecretRepository`): a `TimeoutException`, `IOException`,
  `SocketException` or a cancellation the caller did not request in the exception chain, or a `DbException` that is an
  outage (`IsTransient`, a SQLSTATE in class `08`, a timeout, I/O or `Win32Exception` beneath it, or SQLite's
  `SQLITE_BUSY`/`SQLITE_LOCKED`). Any other failure is `CorruptState`, permanent: any other `DbException` (a missing
  table or column, a damaged database file), a row the repository cannot serve, a schema version this build cannot
  read, an identity it refuses, a store the host does not register, and a payload the value protector refuses. A store
  that returns no payload, or a payload without a value, is `CorruptState`. Each is a result, not a
  throw, with a fixed error that carries nothing from the exception; a canceled token still propagates as a
  cancellation. A name `ISecretNameValidator` refuses is `NotFound`, before the repository is read. The full table is
  in spec 188's `contracts/runtime-secret-resolution.md`.
- A shell that does not compose `SecretsWorkflows` cannot resolve secrets: a secret-bound activity waits with an
  activation-failure incident instead of faulting. A shell that composes another `IRuntimeSecretResolver` beside it
  does not activate: the bridge refuses one registered before it, and the activities runtime's startup check refuses
  one registered after it, each naming both.

`Elsa.Secrets.Nuplane` (feature `SecretsNuplane`) is the other bridge over `ISecretValueResolver`: it resolves package
feed credentials for Nuplane.

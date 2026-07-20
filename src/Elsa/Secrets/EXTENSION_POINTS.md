# Secrets Extension Points

The `Elsa.Secrets` package provides default services and shell feature registration for named secret management.

## Groundwork persistence — host selection

`Elsa.Secrets.Persistence.Groundwork` is the first-party durable replacement for `ISecretRepository`; the
contracts in this package remain Groundwork-free. A host selects it through a Groundwork unified provider feature
(`AddGroundworkSqliteUnifiedPersistence`, `AddGroundworkPostgreSqlUnifiedPersistence`,
`AddGroundworkSqlServerUnifiedPersistence`, or `AddGroundworkMongoDbUnifiedPersistence`) or through the matching
shell feature. The selected provider contributes one admitted `IDocumentStore`; this feature contributes the
Secrets manifest and replaces the repository as a scoped service. See the
[storage-composition contract](../../../specs/094-harden-groundwork-stores/contracts/storage-composition.md)
for host-selected manifest/deployment-source rules.

`SecretsStorageManifest` (`elsa-secrets`, schema `1.3.0`) declares one `secret` document kind backed by the
physical entity table `secrets`. It is `TenancyPolicy.Scoped`: name, list, and point reads always use the
access-bound storage scope, never a scope value supplied in secret JSON. `list-filtered` is the exact
scale-bearing bounded-route identity. `list-unfiltered` and `search-filtered` are explicitly ordinary bounded
routes; the latter has provider-bound substring residual predicates and is not advertised as a scale-bearing
indexed scan. A missing route, unsupported predicate, or failed schema admission is a readiness failure—there is
no client-side whole-collection fallback.

All four production provider leaves use the same manifest and runtime composition. SQL Server and PostgreSQL
require their real container-backed host paths; MongoDB requires the transaction-capable replica-set topology
when a selected combined host also claims multi-document atomic behavior. The current package family, including
`Groundwork.Tool`, is `0.0.1-preview.72`; use the same selected deployment source for runtime admission and CLI
validation/plan/status/apply.

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

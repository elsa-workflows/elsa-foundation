# Secrets Core Extension Points

The Secrets core package defines the contracts used by the runtime, API, Studio module, and persistence adapters.

## Contracts

- `ISecretManager`: lifecycle boundary for create, list, update metadata, rotate, test, revoke, and delete. Lifecycle only — runtime value resolution lives on `ISecretValueResolver` (the W18 store-vs-resolver split), so the two are separately overridable.
- `ISecretValueResolver`: runtime boundary for resolving a `SecretReference` into a value at point of use.
- `ISecretRepository`: metadata and version persistence boundary. The default implementation is in-memory; persistence packages can replace it.
- `ISecretStore`: storage provider for a version payload. Built-ins are encrypted local storage and host configuration lookup.
- `ISecretKeyRing`: resolves the active encryption key and any additional decryption keys by key id, enabling master-key rotation without data loss. The default derives its ring from `SecretsOptions` (legacy `EncryptionKey` plus an optional `Keys`/`ActiveKeyId` set) and validates key ids eagerly at startup. See [`docs/secrets-key-rotation.md`](../../../../docs/secrets-key-rotation.md).
- `ISecretTypeProvider`: descriptor and validation provider for secret types.
- `ISecretAuditSink`: audit event sink. The default sink logs each audit record and warns once when auditing is left unconfigured; hosts can replace it (including with the opt-out `NullSecretAuditSink`).

## Safety Rules

- Public metadata models must not expose raw secret values.
- Runtime resolution returns values only through `ISecretValueResolver`.
- Stores must keep raw values out of metadata and audit records.
- Type providers must validate store compatibility before writes.

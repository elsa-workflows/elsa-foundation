# Secrets Core Extension Points

The Secrets core package defines the contracts used by the runtime, API, Studio module, and persistence adapters.

## Contracts

- `ISecretManager`: lifecycle boundary for create, list, update metadata, rotate, test, revoke, and delete. Lifecycle only — runtime value resolution lives on `ISecretValueResolver` (the W18 store-vs-resolver split), so the two are separately overridable.
- `ISecretValueResolver`: runtime boundary for resolving a `SecretReference` into a value at point of use.
- `ISecretRepository`: metadata and version persistence boundary. The default implementation is in-memory; persistence packages can replace it.
- `ISecretStore`: storage provider for a version payload. Built-ins are encrypted local storage and host configuration lookup.
- `ISecretKeyRing`: resolves the active encryption key and any additional decryption keys by key id, enabling master-key rotation without data loss. The default derives its ring from `SecretsOptions` (legacy `EncryptionKey` plus an optional `Keys`/`ActiveKeyId` set) and validates key ids eagerly at startup. See [`docs/secrets-key-rotation.md`](../../../../docs/secrets-key-rotation.md).
- `ISecretTypeProvider`: descriptor and validation provider for secret types.
- `ISecretScopeRegistry`: read-only catalogue of scope suggestions from the active module composition. Its `List()` result is static metadata, not an allowlist or an inventory of stored Secrets.
- `ISecretScopeProvider`: static provider of advisory scope suggestions. Suggestions appear in the tenant-independent `/secrets/descriptors` metadata response; they do not constrain stored scopes, runtime resolution, or authorization.
- `ISecretAuditSink`: audit event sink. The default sink logs each audit record and warns once when auditing is left unconfigured; hosts can replace it (including with the opt-out `NullSecretAuditSink`).

Secret scope suggestions use `SecretScopeDescriptor` (`Name`, `DisplayName`, optional `Description` and `Source`). The registry preserves the descriptor fields and sorts its immutable snapshot by name. It rejects case-insensitive duplicate names with a diagnostic naming the contributing providers. A provider should set `Source` only when it has a friendly contributor label to show; an omitted source stays null. No scope suggestions are built in. Existing secrets and references retain the open optional string contract, including custom or unregistered values.

## Safety Rules

- Public metadata models must not expose raw secret values.
- Runtime resolution returns values only through `ISecretValueResolver`.
- Stores must keep raw values out of metadata and audit records.
- Type providers must validate store compatibility before writes.

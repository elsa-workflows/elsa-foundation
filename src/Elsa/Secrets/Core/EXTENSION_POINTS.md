# Secrets Core Extension Points

The Secrets core package defines the contracts used by the runtime, API, Studio module, and persistence adapters.

## Contracts

- `ISecretManager`: lifecycle boundary for create, list, update metadata, rotate, test, revoke, delete, and payload resolution.
- `ISecretResolver`: runtime boundary for resolving a `SecretReference` into a value at point of use.
- `ISecretRepository`: metadata and version persistence boundary. The default implementation is in-memory; persistence packages can replace it.
- `ISecretStore`: storage provider for a version payload. Built-ins are encrypted local storage and host configuration lookup.
- `ISecretTypeProvider`: descriptor and validation provider for secret types.
- `ISecretAuditSink`: audit event sink. The default sink is a no-op and can be replaced by host governance modules.

## Safety Rules

- Public metadata models must not expose raw secret values.
- Runtime resolution returns values only through `ISecretResolver` or `ISecretManager.ResolvePayloadAsync`.
- Stores must keep raw values out of metadata and audit records.
- Type providers must validate store compatibility before writes.

# Runtime Contract: Secrets Module

## Secret Resolver

Runtime consumers resolve serialized references only at point of use.

```csharp
public interface ISecretResolver
{
    ValueTask<ResolvedSecret> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default);
}
```

Rules:

- Resolve by immutable normalized technical name.
- Return the latest active, non-expired version only.
- Enforce optional type and scope constraints.
- Use safe failure codes: `NotFound`, `Inactive`, `Expired`, `Revoked`, `Deleted`, `TypeMismatch`, `ScopeMismatch`, `StoreUnavailable`, `Unauthorized`, `CorruptState`.
- Do not log or return raw values on failure.

## Secret Manager

Management operations own lifecycle changes and return metadata-only models.

```csharp
public interface ISecretManager
{
    ValueTask<SecretMetadata> CreateAsync(CreateSecretRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata?> FindAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<Page<SecretMetadata>> ListAsync(SecretQuery query, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata> UpdateAsync(string name, UpdateSecretMetadataRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata> RotateAsync(string name, RotateSecretRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata?> RevokeAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<SecretTestResult> TestAsync(string name, CancellationToken cancellationToken = default);
}
```

Rules:

- No method returns current cleartext values.
- `RotateAsync` retires previous active versions and creates a new active version.
- `DeleteAsync` removes store-owned payload material where the selected store supports deletion, then marks the logical secret deleted.
- `TestAsync` reports availability without returning the value.

## Secret Store

Stores own payload persistence or lookup behavior.

```csharp
public interface ISecretStore
{
    SecretStoreDescriptor Descriptor { get; }
    ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default);
    ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(SecretDeleteContext context, CancellationToken cancellationToken = default);
    ValueTask<SecretTestResult> TestAsync(SecretTestContext context, CancellationToken cancellationToken = default);
}
```

Rules:

- Encrypted store supports writing replacement values and reading protected values.
- Configuration store writes reference metadata only and reads from configured application values.
- Store-private payloads are never projected through metadata endpoints.

## Secret Type Provider

Types validate authoring and rotation input.

```csharp
public interface ISecretTypeProvider
{
    SecretTypeDescriptor Descriptor { get; }
    ValueTask<SecretValidationResult> ValidateCreateAsync(CreateSecretRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretValidationResult> ValidateRotateAsync(RotateSecretRequest request, string storeName, CancellationToken cancellationToken = default);
}
```

Built-in types:

- `text`
- `rsa-key`
- `x509-certificate`

## Secret Expression

Expression descriptor:

- Type: `Secret`
- Value shape: `SecretReference`
- Handler dependency: `ISecretResolver`

Rules:

- The expression handler resolves the secret and returns the resolved value converted to the requested return type where possible.
- The saved workflow input stores the `SecretReference` payload, not the resolved value.
- The expression descriptor must appear in the expression descriptor API so Studio can offer the syntax.

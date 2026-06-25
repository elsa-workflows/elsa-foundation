# Data Model: Secrets Module

## Secret

Logical named secret referenced by workflows and modules.

Fields:

- `Id`: stable internal identifier.
- `Name`: normalized immutable technical name; unique among non-deleted secrets.
- `DisplayName`: operator-facing label.
- `Description`: optional operator-facing explanation.
- `TypeName`: registered secret type name.
- `StoreName`: registered secret store name.
- `Scope`: optional compatibility scope.
- `Tags`: optional case-insensitive tags.
- `Status`: `Active`, `Revoked`, or `Deleted` at the logical secret level.
- `CreatedAt`: creation timestamp.
- `UpdatedAt`: optional last metadata/lifecycle timestamp.
- `Versions`: ordered secret versions.

Validation rules:

- `Name` is required, normalized, and immutable after creation.
- Active secrets must have exactly one latest active, non-expired version.
- Deleted secrets are excluded from ordinary list and picker results.
- Store/type compatibility must be valid before a version is written.

State transitions:

```text
Create -> Active
Active -> Active     (metadata update)
Active -> Active     (rotation retires previous active versions and adds a new active version)
Active -> Revoked
Active -> Deleted
Revoked -> Deleted
```

## SecretVersion

A concrete version of the secret's store payload.

Fields:

- `Version`: monotonically increasing integer within a secret.
- `Status`: `Active`, `Retired`, `Expired`, or `Revoked`.
- `CreatedAt`: version creation timestamp.
- `ExpiresAt`: optional expiration timestamp.
- `Payload`: store-owned payload metadata.

Validation rules:

- Rotation uses `max(Version) + 1`.
- Only one non-expired version may be active for a healthy active secret.
- Expired, retired, and revoked versions cannot resolve.

## SecretPayload

Store-owned payload shape used inside a secret version.

Fields:

- `Value`: transient input/output value used only between manager and store operations; not persisted in metadata responses.
- `Metadata`: case-insensitive store-owned metadata.

Rules:

- Encrypted store persists protected value metadata, never raw `Value`.
- Configuration store persists lookup metadata such as configuration key, never the configured value.
- General metadata APIs never expose provider-private payload metadata.

## SecretReference

Serializable reference stored by workflows or module settings.

Fields:

- `Name`: immutable secret technical name.
- `TypeName`: optional type constraint.
- `Scope`: optional scope constraint.

Rules:

- Resolution must load by `Name`.
- If `TypeName` or `Scope` is present, the resolved secret must match it.
- References never contain raw values.

## SecretStoreDescriptor

Safe metadata describing a store.

Fields:

- `Name`
- `DisplayName`
- `Description`
- `Capabilities`: read/write/delete/test/export/versioned flags.
- `IsReadOnly`: true when the store does not let Elsa mutate underlying values.

## SecretTypeDescriptor

Safe metadata describing a secret type.

Fields:

- `Name`
- `DisplayName`
- `Description`
- `EditorHint`
- `SupportedStoreNames`

## SecretQuery

Filtering and paging shape used by list and picker flows.

Fields:

- `Search`
- `TypeName` / `TypeNames`
- `StoreName` / `StoreNames`
- `Scope`
- `Status`
- `ActiveOnly`
- `Page`
- `PageSize`

Rules:

- Defaults exclude deleted secrets.
- Picker defaults to active-only.
- Page size is bounded by implementation-defined maximum.

## SecretOperationAuditRecord

Audit-ready record for privileged secret actions.

Fields:

- `Operation`: create, update metadata, rotate, revoke, delete, test, resolve, import, export.
- `SecretName`
- `ActorId`: optional until authorization integration supplies it.
- `Timestamp`
- `Outcome`
- `Reason`: optional safe reason.

Rules:

- Must not contain raw values, protected values, or provider-private payload metadata.

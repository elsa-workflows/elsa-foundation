# Data Model: Secrets API Minimal API Migration

## Request authority

- `TenantId`: required normalized-principal claim for every data operation; never accepted from transport input.
- `Name`: route-authoritative secret identity for get, update, rotate, revoke, delete, and test.
- `Create body`: owns the new name plus safe metadata, type/store/scope/tags, sensitive value or configuration key, expiry, and provider metadata.
- `Update body`: owns display name and description only.
- `Rotate body`: owns sensitive replacement value or configuration key, expiry, and provider metadata only.
- `List query`: search, singular/plural type and store filters, scope, status, active-only, page, and page size.
- `Picker body`: search, type/store collections, scope, and active-only; server retains the current bounded page behavior.

Route and principal sources take precedence over any similarly named JSON member.

## Safe secret metadata

- Identity: id, tenant id, normalized name.
- Safe description: display name and optional description.
- Classification: type name, store name, optional scope, tags.
- Lifecycle: status, current version, created/updated/expiry timestamps.
- Explicit exclusions: raw value, configuration key, protected payload, provider-private metadata, and active version payload.

The existing `SecretMetadata` remains the public projection. Transport work does not add sensitive fields.

## Lifecycle states and visibility

- `Active`: metadata visible and value may be usable subject to type/scope/expiry policy.
- `Revoked` or retired version: metadata visibility follows existing public policy; runtime resolution is denied.
- `Expired`: metadata remains governed by existing list/get filters; runtime use is denied.
- `Deleted`: excluded from ordinary list, get, and picker operations and treated as not found across tenants.

The migration preserves current state transitions and does not redesign persistence or revision conflict handling.

## Descriptor and picker projections

- `SecretDescriptorsResponse`: ordered safe type descriptors and store descriptors; tenant-independent after read authorization.
- `SecretPickerResponse`: bounded safe metadata items plus the current `CanCreateInline` value.

## Permission ownership

- Owner: `Elsa.Secrets.Api`.
- Catalog entries: read, write, update-value, delete, test, use, import, export.
- Implication: write implies read.
- Independent grants: update-value, delete, test, use, import, export.
- Administrative wildcard: explicit grant branch only; never a catalog entry.

Each HTTP endpoint declares exactly one canonical `Any(*, action)` policy and one permission security disposition.

## Endpoint contract record

- `Route`: exact shell-relative source template under `/secrets`.
- `Method`: GET, POST, PUT, or DELETE as currently registered.
- `Owner`: `Elsa.Secrets.Api`.
- `AuthoringModel`: Minimal API after migration.
- `PermissionPolicy`: wildcard plus one action permission.
- `TenantRequirement`: required for nine data operations; intentionally absent from descriptors.
- `Request/response metadata`: explicit binding and consumed OpenAPI facts.

Exactly ten records may be active per shell generation.

## Compatibility evidence set

- `Manifest`: canonical route, method, owner, authoring, security, content, and response metadata.
- `HTTP`: named observations for authorization, tenant, filtering, lifecycle, validation/conflict, malformed input, redaction, and safe errors.
- `OpenAPI`: canonical projection of the ten consumed operations from the actual generated document.
- `ApprovedDifferences`: exact reviewed records; expected to remain empty.
- `VolatilityRules`: narrow normalization for generated ids/timestamps/traces with separate presence/validity assertions.

Ten unchanged captures must serialize identically.

## Unload evidence

- `Cycle`: isolated load, map, materialize, exercise, document, release, unload, and bounded collection attempt.
- `Stage`: routes, services, serializer, documentation, harness, or clean.
- `WeakReferences`: load context, assembly, and representative API type only.
- `Collected`: result after every owned strong reference is released.
- `Diagnostic`: stage/owner classification when collection fails.

Materialized route and service stages must collect. Serializer/documentation retention is reported explicitly and cannot be hidden by reflection-only checks.

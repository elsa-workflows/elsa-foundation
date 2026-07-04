# Extension points — Foundation Identity Groundwork persistence

The Groundwork document-store bridge that makes the Foundation identity domain durable. It replaces the
in-memory identity stores (`InMemoryIdentityStore`) with Groundwork-backed stores so users, roles, external
identities, and tenant memberships survive process restarts. The IAM store contracts themselves
(`IUserStore`, `IRoleStore`, `IExternalIdentityStore`, `ITenantMembershipStore`) are owned by
[`Foundation Identity Abstractions`](../../Abstractions/EXTENSION_POINTS.md); this feature is a concrete,
overridable persistence provider for them.

## Provider selection — host composition

| Shell feature | Scope | Registration |
|---|---|---|
| `IdentityGroundworkPersistence` | Server runtime | `IdentityGroundworkPersistenceFeature` → `AddGroundworkIdentityStores()` |

`AddGroundworkIdentityStores()` calls `RemoveAll` for each IAM store contract, then registers the
Groundwork-backed store as a singleton. Registration is override-friendly: a host that composes this
feature and then registers its own store still wins.

## Persisted document kinds

The feature owns its own `IdentityStorageManifest` (identity `elsa-identity`, owner `elsa.identity`,
schema `1.0.0`) rather than folding identity kinds into the runtime document manifest. Each document kind
is a **wire-safe, stable persistence identifier** and must never be renamed without a schema migration and
a golden-fixture bump (see [`../../../../../../docs/serialization.md`](../../../../../../docs/serialization.md)).

| Document kind | Backing record | Primary key | Declared index |
|---|---|---|---|
| `identityUser` | `UserRecord` | `tenantId:userId` | `by-email` (`emailKey`) |
| `identityRole` | `RoleRecord` | `tenantId:roleId` | `by-tenant` (`tenantKey`) |
| `identityExternalIdentity` | `ExternalIdentityRecord` | `tenantId:provider:providerSubject` | `by-user` (`userKey`) |
| `identityTenantMembership` | `TenantMembershipRecord` | `tenantId:userId` | `by-tenant` (`tenantKey`) |

Composite ids are escaped and case-normalized so a separator inside a part can never forge a different key
and lookups stay case-insensitive (matching the in-memory store). The frozen `IdentityGroundworkJson`
options serialize enums as strings and round-trip the records' `IReadOnlySet<string>` collections via
`ReadOnlyStringSetJsonConverter` (sorted for deterministic output).

## Schema evolution

The document shapes are frozen by a golden-fixture drift test (`Fixtures/v1/*.json`). Evolving a shape
requires, in the same change: bump `IdentityStorageManifest.SchemaVersion` (add a reader/upcaster for the
old shape if needed), regenerate the golden fixture (`GROUNDWORK_FIXTURE_REGEN=1`), and keep the old
fixtures so historical documents still load.

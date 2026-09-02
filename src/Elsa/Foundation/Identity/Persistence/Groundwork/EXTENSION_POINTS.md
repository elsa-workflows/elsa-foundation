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
| `FoundationIdentityAspNetCoreIdentityGroundwork` | Server runtime | `AspNetCoreIdentityGroundworkFeature` → `AddFoundationAspNetCoreIdentityGroundwork()` |

`AddGroundworkIdentityStores()` calls `RemoveAll` for each IAM store contract, then registers the
Groundwork-backed store as scoped. Registration is override-friendly: a host that composes this
feature and then registers its own store still wins.

For ASP.NET Core Identity hosts, select `FoundationIdentityAspNetCoreIdentityGroundwork` instead of
the lower-level IAM persistence feature directly. It registers the framework-facing
`UserManager`/`RoleManager` stores and the Elsa IAM adapters over one authoritative Groundwork Identity
authority. Groundwork is the sole first-party Elsa Identity persistence authority; a host-owned integration
must be explicitly selected when replacing it.

## Persisted document kinds

The feature owns its own `IdentityStorageManifest` (identity `elsa-identity`, owner `elsa.identity`,
schema `1.0.6`) rather than folding identity kinds into the runtime document manifest. Each document kind
is a **wire-safe, stable persistence identifier** and must never be renamed without a schema migration and
a golden-fixture bump (see [`../../../../../../docs/serialization.md`](../../../../../../docs/serialization.md)).

| Document kind | Scope | Primary responsibility |
|---|---|---|
| `identityUser`, `identityRole`, `identityApplication`, `identityCredential`, `identityClaimMapping` | Scoped | Tenant identity authority and its application/credential/claim-mapping records |
| `identityProviderConfiguration` | Scoped | Tenant-local provider configuration |
| `identityGlobalProviderConfiguration` | Global | Deliberately host-wide provider configuration; it is the sole global Identity unit |
| `identityUserClaim`, `identityRoleClaim`, `identityExternalLogin`, `identityUserRole`, `identityUserToken` | Scoped | User/role relationship records |
| `identityTenantMembership`, `identityUserNameReservation`, `identityEmailReservation`, `identityRoleNameReservation`, `identityMutationReceipt` | Scoped | Membership, uniqueness reservation, and atomic mutation-receipt records |

Composite ids are escaped and case-normalized so a separator inside a part can never forge a different key
and lookups stay case-insensitive (matching the in-memory store). The frozen `IdentityGroundworkJson`
options serialize enums as strings and round-trip the records' `IReadOnlySet<string>` collections via
`ReadOnlyStringSetJsonConverter` (sorted for deterministic output).

The ASP.NET Core Identity Groundwork provider uses physicalized identity authority documents. Its exact declared
bounded-route identities are `find-user-by-normalized-name`, `find-user-by-normalized-email`,
`find-role-by-normalized-name`, `list-roles-by-tenant`, `list-user-claims`, `find-users-by-claim`,
`list-role-claims`, `list-user-roles`, `list-role-users`, `list-user-logins`,
`list-claim-mappings-by-provider`, and `list-expired-mutation-receipts`. External-login subject, token,
tenant-membership, reservation, and provider-configuration point lookups use deterministic primary IDs.
The global configuration unit must be acquired with global access; a global storage unit never grants
privileged write authority by itself. Unsupported provider topology, missing schema, or a
missing required bounded route is a readiness failure; runtime code must not silently fall back to whole-document
scans.

## Authority aggregates and admission bounds

User and role authority roots own explicit registries of their relationship documents. Root saves change
normalized-name reservations (and email reservations when the shared uniqueness policy enables them) in the
same atomic Groundwork unit of work. Aggregate deletion follows those registries, validates every registered
child's tenant and owner, updates affected opposite-owner registries, and deletes children, reservations, and
the root under one mutation receipt. A missing or foreign registered child is an integrity failure; it is
never silently skipped.

Mutation receipts remain replayable only through their declared expiry instant. The writer rejects and
reclaims an expired exact receipt by observed version before reusing its deterministic id. It also performs
amortized cleanup through the declared `list-expired-mutation-receipts` route. Each tenant triggers cleanup
after 32 mutation attempts or five elapsed minutes, whichever comes first, and deletes at most 64 oldest
expired receipts. Cleanup therefore has bounded work while its sustained drain capacity exceeds receipt
creation. It deletes receipts directly and never creates receipts for receipt deletion.

Relationship growth is admitted only while the combined distinct registry count for one user or role remains
at or below `IdentityStorageManifest.MaxAggregateRelationshipEntries` (512). This admission contract reuses
the provider-matrix-proven bounded relationship page envelope; it is not a claim about any provider's maximum.
It is enforced only on growth, so an oversized aggregate
from repair/import tooling can still remove relationships and be brought back within the supported envelope.

## Schema evolution

This is a clean-break Groundwork v2 store. `IdentityV2StorageManifest` is the one declaration authority and the
runtime does not load, upcast, dual-write, or migrate legacy Groundwork documents. Any declaration change must
update the v2 manifest contract tests and pass the four-provider Identity matrix from a fresh database.

## Provider connection and topology

Identity contributes its public v2 storage units directly; selecting Identity does not select a second legacy
deployment schema. The host registers exactly one public provider connection for the target, then registers the
Identity feature. For SQLite:

```csharp
services.AddGroundworkStorageProviderConnection(
    _ => new SqliteProviderFactory().Create(connectionString));
services.AddFoundationAspNetCoreIdentityGroundwork();
```

Use the equivalent `PostgreSqlProviderFactory`, `SqlServerProviderFactory`, or `MongoProviderFactory` for the
other supported providers. `GroundworkStorageSessionSource` applies the declared Identity units during host or
shell admission and fails when no matching provider connection is registered. MongoDB deployments require a
transaction-capable replica set because Identity mutations span multiple units atomically; standalone MongoDB
must be refused rather than degraded to partial writes.

Groundwork packages are restored from the Valence Works Feedz source:
`https://f.feedz.io/valence-works/groundwork/nuget/index.json`. This clean-break path does not publish or install
Groundwork packages from NuGet.org.

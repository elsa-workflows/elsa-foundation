# Extension points — Foundation Identity persistence

Foundation Identity persistence supplies replaceable durable implementations of the IAM store contracts.
Groundwork remains the default implementation for the complete Identity authority. The opt-in EF Core features
can independently replace tenant/global provider configurations or the complete tenant-local IAM authority,
including users, roles, relationships, memberships, applications, and credentials. The IAM store contracts themselves
(`IUserStore`, `IRoleStore`, `IExternalIdentityStore`, `ITenantMembershipStore`) are owned by
[`Foundation Identity Abstractions`](../../Abstractions/EXTENSION_POINTS.md); these persistence features supply
concrete, overridable implementations for them.

## Provider selection — host composition

| Shell feature | Scope | Registration |
|---|---|---|
| `IdentityGroundworkPersistence` | Server runtime | `IdentityGroundworkPersistenceFeature` → `AddGroundworkIdentityStores()` |
| `IdentityProviderConfigurationEntityFrameworkCore` | Server runtime; provider configuration only | `IdentityProviderConfigurationEntityFrameworkCoreFeature` → `AddIdentityProviderConfigurationEntityFrameworkCore()` |
| `IdentityIamEntityFrameworkCore` | Server runtime; complete tenant-local IAM authority | `IdentityIamEntityFrameworkCoreFeature` → `AddIdentityIamEntityFrameworkCore()` |
| `FoundationIdentityAspNetCoreIdentityGroundwork` | Server runtime | `AspNetCoreIdentityGroundworkFeature` → `AddFoundationAspNetCoreIdentityGroundwork()` |
| `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` | Server runtime; opt-in ASP.NET Identity adapter over the shared EF authority | `AspNetCoreIdentityEntityFrameworkCoreFeature` → `AddFoundationAspNetCoreIdentityEntityFrameworkCore()` |

`AddGroundworkIdentityStores()` registers the Groundwork-backed stores as scoped. When an EF feature is not
selected, Groundwork owns its corresponding contracts as part of that default. Selecting
`IdentityProviderConfigurationEntityFrameworkCore` replaces only `IProviderConfigurationStore` and
`IRevisionAwareProviderConfigurationStore`; the backend marker preserves that selection regardless of whether
the Groundwork or EF registration call runs first. Selecting `IdentityIamEntityFrameworkCore` replaces the
complete tenant-local authority as one ownership group. Each backend group is independently
order-safe. Repeating an equivalent EF registration is idempotent, while an incompatible provider or connection
registration fails before partially mutating the service collection.
The host must reference the selected EF provider package and provide the matching SQLite, SQL Server,
PostgreSQL, or MySQL options. Schema migration and default host selection remain separate rollout gates.

For ASP.NET Core Identity hosts, select either `FoundationIdentityAspNetCoreIdentityGroundwork` or the opt-in
`FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` adapter instead of composing the lower-level authority
and framework stores independently. Each registers `UserManager`/`RoleManager` over one authoritative persistence
family. Groundwork remains the default first-party Elsa Identity persistence authority until the later rollout flip.
The EF replacement is governed by
[ADR 0073](../../../../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md);
a host-owned integration must be explicitly selected when replacing it.

## Overridable contracts

| Contract | Layer | Default implementation | Opt-in implementation | Selection behavior |
|---|---|---|---|---|
| `IUserStore`, `IRevisionAwareUserStore` | *Feature contracts — Elsa.Foundation.Identity.Abstractions* | `GroundworkUserStore` | `EfUserStore` | Complete-authority selection; preserves tenant-local normalized lookup, reservations, aggregate atomicity, and CAS |
| `IRoleStore`, `IRevisionAwareRoleStore`, `IPagedRoleStore` | *Feature contracts — Elsa.Foundation.Identity.Abstractions* | `GroundworkRoleStore` | `EfRoleStore` | Complete-authority selection; preserves stable bounded enumeration, reservations, and CAS |
| `IClaimMappingStore`, `IRevisionAwareClaimMappingStore`, `IPagedClaimMappingStore` | *Feature contracts — Elsa.Foundation.Identity.Abstractions* | `GroundworkClaimMappingStore` | `EfClaimMappingStore` | Complete-authority selection; preserves provider-local ordering and revisions |
| `IExternalIdentityStore`, `IRevisionAwareExternalIdentityStore`, `IPagedExternalIdentityStore` | *Feature contracts — Elsa.Foundation.Identity.Abstractions* | `GroundworkExternalIdentityStore` | `EfExternalIdentityStore` | Complete-authority selection; preserves subject uniqueness, ownership/rebind rules, bounded ordering, and atomic owner updates |
| `ITenantMembershipStore`, `IRevisionAwareTenantMembershipStore` | *Feature contracts — Elsa.Foundation.Identity.Abstractions* | `GroundworkTenantMembershipStore` | `EfTenantMembershipStore` | Complete-authority selection; preserves tenant isolation, lossless sets, and atomic user ownership |
| `IApplicationStore` | *Feature contract — Elsa.Foundation.Identity.Abstractions* | `GroundworkApplicationStore` | `EfApplicationStore` | Complete-authority selection; preserves lossless records and normalized lookup |
| `IRevisionAwareApplicationStore` | *Feature contract — Elsa.Foundation.Identity.Abstractions* | Same scoped `GroundworkApplicationStore` | Same scoped `EfApplicationStore` | Uses the selected scoped store and preserves opaque create-only/CAS revisions |
| `ICredentialStore` | *Feature contract — Elsa.Foundation.Identity.Abstractions* | `GroundworkCredentialStore` | `EfCredentialStore` | Complete-authority selection; preserves hashed-secret-only persistence and normalized lookup |
| `IRevisionAwareCredentialStore` | *Feature contract — Elsa.Foundation.Identity.Abstractions* | Same scoped `GroundworkCredentialStore` | Same scoped `EfCredentialStore` | Uses the selected scoped store and preserves opaque create-only/CAS revisions |
| `IProviderConfigurationStore` | *Feature contract — Elsa.Foundation.Identity.Abstractions* | `GroundworkProviderConfigurationStore` in `Elsa.Foundation.Identity.Persistence.Groundwork` | `EfProviderConfigurationStore` in `Elsa.Foundation.Identity.Persistence.EntityFrameworkCore` | Exactly one backend wins; the EF feature replaces only this contract and its revision-aware companion |
| `IRevisionAwareProviderConfigurationStore` | *Feature contract — Elsa.Foundation.Identity.Abstractions* | `GroundworkProviderConfigurationStore` in `Elsa.Foundation.Identity.Persistence.Groundwork` | `EfProviderConfigurationStore` in `Elsa.Foundation.Identity.Persistence.EntityFrameworkCore` | Uses the same scoped store instance and preserves the opaque `gw:` revision contract |

Override these contracts when a host owns a different Identity persistence boundary. Consumers depend only on
the abstractions; implementations must preserve the scope checks, record fidelity, lookup, unconditional upsert,
and optimistic compare-and-swap semantics documented by each contract group.

## Implementable contributor interfaces

This persistence feature exposes no additive contributor interfaces.

## Events

This persistence feature publishes no domain events.

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

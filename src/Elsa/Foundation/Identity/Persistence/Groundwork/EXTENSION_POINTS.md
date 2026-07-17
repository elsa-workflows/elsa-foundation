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
authority. Do not compose it with `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore`; that would
select two concrete authorities for the same identity surface.

## Persisted document kinds

The feature owns its own `IdentityStorageManifest` (identity `elsa-identity`, owner `elsa.identity`,
schema `1.0.4`) rather than folding identity kinds into the runtime document manifest. Each document kind
is a **wire-safe, stable persistence identifier** and must never be renamed without a schema migration and
a golden-fixture bump (see [`../../../../../../docs/serialization.md`](../../../../../../docs/serialization.md)).

| Document kind | Backing record | Primary key | Declared index |
|---|---|---|---|
| `identityUser` | `UserRecord` | `tenantId:userId` | `by-email` (`emailKey`) |
| `identityRole` | `RoleRecord` | `tenantId:roleId` | `by-tenant` (`tenantKey`) |
| `identityExternalIdentity` | `ExternalIdentityRecord` | `tenantId:provider:providerSubject` | `by-user` (`userKey`) |
| `identityTenantMembership` | `TenantMembershipRecord` | `tenantId:userId` | none; exact primary-ID reads |
| `identityMutationReceipt` | atomic mutation outcome | deterministic mutation receipt id | oldest-expired (`expiresAt`) |

Composite ids are escaped and case-normalized so a separator inside a part can never forge a different key
and lookups stay case-insensitive (matching the in-memory store). The frozen `IdentityGroundworkJson`
options serialize enums as strings and round-trip the records' `IReadOnlySet<string>` collections via
`ReadOnlyStringSetJsonConverter` (sorted for deterministic output).

The ASP.NET Core Identity Groundwork provider uses physicalized identity authority documents and exactly
10 live scale-bearing application routes for normalized user name, normalized email, normalized role name,
tenant role listing, claims, user-role, role-user, and login-by-user lookups. External-login subject, token, and
tenant-membership and reservation lookups use deterministic primary IDs; receipt cleanup has one separate bounded
oldest-expired maintenance route. Unsupported provider topology, missing schema, or a
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

The document shapes are frozen by a golden-fixture drift test (`Fixtures/v1/*.json`). Evolving a shape
requires, in the same change: bump `IdentityStorageManifest.SchemaVersion` (add a reader/upcaster for the
old shape if needed), regenerate the golden fixture (`GROUNDWORK_FIXTURE_REGEN=1`), and keep the old
fixtures so historical documents still load.

## Deployment schema and topology

Identity is not included in the default provider substrate schema. A host that selects ASP.NET Core Identity
Groundwork must select the matching runtime deployment schema as well as the Identity feature. Code-composed
hosts use the provider's generic unified registration; for example:

```csharp
services.AddGroundworkSqliteUnifiedPersistence<GroundworkAllFeaturesWithIdentityDeploymentSchema>(connectionString);
services.AddFoundationAspNetCoreIdentityGroundwork();
```

The same generic deployment-schema overload is available for PostgreSQL, SQL Server, and MongoDB. Provider
registration still contributes only the six provider-level families; the explicit Identity feature contributes
its own services and manifest. Validate/apply that same Identity deployment schema in the deployment pipeline:

```bash
dotnet groundwork validate \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesWithIdentityDeploymentSchema \
  --provider <sqlite|sqlserver|postgresql|mongodb> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION
```

Use the same `--manifest-type` for `plan`, `status`, and `apply`. `validate`, `plan`, and `status` are
read-only; `apply` is the only command that may change the target database. MongoDB deployments that rely on
multi-document identity guarantees require a transaction-capable replica set or sharded topology.

# ASP.NET Core Identity and OpenIddict Groundwork Contract Inventory

Date: 2026-07-12.

Status: completed research for [issue #631](https://github.com/elsa-workflows/elsa-foundation/issues/631). This report inventories the implementation boundary; it does not freeze production-store APIs or implement stores.

Program goal: [Zero-EF Persistence](../program-goals/zero-ef-persistence.md).

Decision: [ADR 0042](../adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md).

## Executive Finding

Replacing both EF integrations is feasible without adding Groundwork to Elsa's core identity contracts. The concrete packages can implement the framework-facing store interfaces over Groundwork documents while Elsa's `IUserStore`, `IRoleStore`, `IExternalIdentityStore`, and `ITenantMembershipStore` remain provider-neutral.

It is not yet an adapter-only change. Groundwork must first close five load-bearing gaps:

1. queryable compound indexes and range/date predicates;
2. tenant enforcement on load, save, update, delete, and query, not query filtering alone;
3. efficient bulk prune/revoke operations;
4. a deliberate answer for OpenIddict's generic `IQueryable` delegate overloads without general LINQ or load-all fallback;
5. four-provider transactional and optimistic-concurrency conformance for relationship updates.

ASP.NET Core Identity does not require a specialized storage primitive. Its records fit dedicated/entity document tables plus ordinary atomic units of work. OpenIddict's four records also fit entity document tables, but its bulk cleanup and generic query contracts require a small specialized adapter/execution seam above Groundwork's bounded document query.

## Evidence Baseline

The inventory was verified against the repository at this report's date and these resolved packages:

- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` and EF Core `10.0.8`;
- `OpenIddict.Abstractions`, `OpenIddict.AspNetCore`, and `OpenIddict.EntityFrameworkCore` `7.5.0`;
- the .NET `10.0.8` shared-framework Identity store interfaces;
- the local official OpenIddict 7.5.0 assemblies/XML documentation and the package's concrete EF models/stores;
- the current Groundwork `main` document, query, index, concurrency, tenancy, and unit-of-work contracts.

The current host activates `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` and `FoundationIdentityOpenIddict` in [shells.json](../../src/Apps/Elsa.Server/shells.json). Production configuration points both at SQLite in [shells.Production.json](../../src/Apps/Elsa.Server/shells.Production.json). The host references the EF Identity project directly, while the OpenIddict project contains EF packages and its EF context/migrations itself.

## Current Composition And Registration Seams

### Existing Groundwork IAM Persistence

Elsa already ships `IdentityGroundworkPersistenceFeature` in `Elsa.Foundation.Identity.Persistence.Groundwork`. It registers `GroundworkUserStore`, `GroundworkRoleStore`, `GroundworkExternalIdentityStore`, and `GroundworkTenantMembershipStore` for Elsa's four provider-neutral IAM abstractions and declares its own `IdentityStorageManifest` over `UserRecord`, `RoleRecord`, external-identity, and tenant-membership documents.

That feature does not implement ASP.NET Core Identity or OpenIddict framework stores, so it does not by itself remove either EF lane. It also cannot be registered unchanged beside the proposed framework-facing feature: doing so would create competing service registrations and two user/role authorities (`UserRecord`/`RoleRecord` versus `AspNetCoreIdentityUser`/framework role records).

The implementation slice must therefore evolve or retire the existing feature explicitly. The target is one authority per concept:

- framework user and role documents are authoritative for identities, credentials, normalized lookup fields, claims, roles, and external logins;
- Elsa `IUserStore`, `IRoleStore`, and `IExternalIdentityStore` become adapters over those same documents and unit of work;
- tenant membership remains an Elsa-owned document where its separate lifecycle is useful, but participates in the same Groundwork unit of work when changed with identity state;
- the legacy `IdentityStorageManifest` units and registrations that duplicate those concepts are removed or revised before the new feature is enabled.

Because this product is greenfield, no data bridge from the legacy Groundwork IAM document shapes is required. Existing store algorithms, manifest declarations, and conformance tests should still be reused where their behavior matches the framework inventory.

### ASP.NET Core Identity

`AddFoundationAspNetCoreIdentityEntityFrameworkCore` currently:

1. registers the provider-neutral Elsa identity services;
2. registers `ApplicationIdentityDbContext`;
3. calls `AddIdentityCore<AspNetCoreIdentityUser>()`, `AddRoles<IdentityRole>()`, `AddSignInManager()`, and `AddDefaultTokenProviders()`;
4. calls `AddEntityFrameworkStores<ApplicationIdentityDbContext>()`;
5. replaces Elsa's four in-memory IAM stores with EF adapters over the same context;
6. registers the configured admin seeder under both `IHostedService` and CShells `IShellInitializer`.

The Groundwork replacement should preserve the provider-neutral feature, cookie scheme, managers, principal factory, sign-in service, safety guard, and lifecycle hooks. It replaces only the EF feature/package, store registrations, migration bootstrapping, and EF IAM adapters.

Recommended seam:

```text
FoundationIdentityAspNetCoreIdentity
    + FoundationIdentityAspNetCoreIdentityGroundwork
        -> AddIdentityCoreServices(...)
        -> AddUserStore<GroundworkIdentityUserStore>()
        -> AddRoleStore<GroundworkIdentityRoleStore>()
        -> replace Elsa IAM stores with Groundwork adapters
        -> Groundwork schema validate/apply lifecycle
        -> provider-neutral admin seeder
```

The seeder must stop resolving an EF `DbContext`; schema readiness belongs to Groundwork's startup/CLI policy. Its configured-credential validation, role/user idempotency, permission catalog expansion, `*` grant, secret-safe logging, and dual lifecycle registration remain unchanged.

### OpenIddict

`AddFoundationIdentityOpenIddict` currently:

1. registers `OpenIddictIdentityDbContext` with in-memory EF for development/demo or SQLite otherwise;
2. calls `AddOpenIddict().AddCore(core => core.UseEntityFrameworkCore()...)`;
3. registers the server custom first-party flow;
4. registers local validation with token-entry validation enabled;
5. registers `OpenIddictTokenService` over `IOpenIddictTokenManager`;
6. registers an EF store initializer under both lifecycle hooks.

The Groundwork seam should keep server, validation, scheme selection, key/lifetime configuration, and `OpenIddictTokenService` unchanged. Replace `UseEntityFrameworkCore`, the `DbContext`, EF initializer, EF migrations, and EF package references with OpenIddict core store/resolver registrations backed by Groundwork and the shared Groundwork schema lifecycle.

## ASP.NET Core Identity Store Inventory

The .NET 10 EF `UserStore` currently registered by `AddEntityFrameworkStores` advertises every interface below. The first column distinguishes direct framework-manager use from storage shapes Elsa's separate IAM adapters currently exercise. Those IAM adapters access the shared EF sets directly; that does not make an otherwise optional framework interface mandatory.

| Interface | Current activation | Required operations / notes |
|---|---|---|
| `IUserStore<AspNetCoreIdentityUser>` | Required and exercised | Create/update/delete; id/name and normalized-name accessors; find by id and normalized name. |
| `IUserPasswordStore<TUser>` | Exercised | Set/get password hash and detect password presence. Admin creation and password sign-in depend on it. |
| `IUserSecurityStampStore<TUser>` | Activated by managers/token providers | Set/get security stamp. Preserve for password/token invalidation even though Elsa has no dedicated security-stamp endpoint. |
| `IUserEmailStore<TUser>` | Exercised | Set/get/confirm email, normalized email, find by normalized email. Login accepts either username or email. |
| `IUserLockoutStore<TUser>` | Exercised by `CheckPasswordSignInAsync(..., lockoutOnFailure: true)` | Lockout end/enabled, access-failure count, increment/reset. Updates require optimistic concurrency. |
| `IUserPhoneNumberStore<TUser>` | Registered optional capability | Phone and confirmation state; needed by the default phone token provider if used. No Elsa endpoint currently exercises it. |
| `IUserTwoFactorStore<TUser>` | Registered optional capability | Set/get two-factor enabled. Not currently exercised by Elsa endpoints. |
| `IUserLoginStore<TUser>` | Exercised indirectly and shared with Elsa external identities | Add/remove/list logins; find by `(LoginProvider, ProviderKey)`. |
| `IUserClaimStore<TUser>` | Exercised through Elsa IAM projection | Add/replace/remove/list claims and find users for a claim. Direct permissions are permission claims. |
| `IUserRoleStore<TUser>` | Exercised through Elsa IAM projection | Add/remove membership; list/check roles; find users in a role. |
| `IUserAuthenticationTokenStore<TUser>` | Registered by the full store | Set/get/remove `(UserId, LoginProvider, Name)` tokens. Default providers may use it. |
| `IUserAuthenticatorKeyStore<TUser>` | Registered optional capability | Set/get authenticator key, conventionally in the user-token store. |
| `IUserTwoFactorRecoveryCodeStore<TUser>` | Registered optional capability | Replace, redeem, and count recovery codes, conventionally in the user-token store. |
| `IQueryableUserStore<TUser>` | Advertised by EF; one test uses `Users` | Not required by Elsa's production sign-in or seed algorithm. The Groundwork store should not implement it; change the seed assertion to bounded lookup. |
| `IUserPasskeyStore<TUser>` | Advertised by the .NET 10 EF store | Elsa does not exercise passkeys, and the committed Elsa migration contains no passkey table. Treat passkeys as an explicit later capability; do not claim support merely because the EF generic store advertises the interface. |
| `IProtectedUserStore<TUser>` | Marker advertised by EF | Marker only; do not advertise unless the replacement provides equivalent protected-personal-data behavior. |

The role store advertises:

| Interface | Required operations / notes |
|---|---|
| `IRoleStore<IdentityRole>` | Create/update/delete; role id/name and normalized-name accessors; find by id/name. |
| `IRoleClaimStore<IdentityRole>` | Add/remove/list role claims. Elsa permissions are role claims today. |
| `IQueryableRoleStore<IdentityRole>` | Optional and not used by Elsa production code. Do not implement it in the bounded Groundwork adapter. |

`UserManager` discovers optional capabilities with interface checks. The Groundwork implementation must advertise only interfaces it implements completely; a registered interface that later throws `NotSupportedException` is a false capability claim.

### Identity Shapes, Indexes, And Relationships

The current effective shapes are:

- user: Identity fields plus `TenantId` and `DisplayName`;
- role: Identity role fields; the EF Elsa adapter currently encodes tenancy as `{tenantId}:{roleName}`;
- user claim and role claim;
- external login;
- user-role membership;
- user authentication token;
- Elsa tenant membership, including role ids and direct permissions.

Required indexes:

| Shape | Key / index | Purpose |
|---|---|---|
| User | id | Primary lookup and relationship target. |
| User | unique `(TenantId, NormalizedUserName)` | Tenant-safe username lookup and uniqueness. |
| User | `(TenantId, NormalizedEmail)` | Tenant-safe email lookup; uniqueness remains false because `RequireUniqueEmail` is false. |
| Role | id | Primary lookup and relationship target. |
| Role | unique `(TenantId, NormalizedName)` | Replace encoded role names with explicit tenant ownership. |
| User login | unique `(LoginProvider, ProviderKey)` plus `UserId` | External subject lookup and per-user listing. Add tenant to the unique key if the same provider subject may be linked independently per tenant. |
| User-role | unique `(UserId, RoleId)` plus `RoleId` | Membership check/list in both directions. |
| User/role claim | owner id, optionally `(OwnerId, ClaimType)` | Claim and permission projection. |
| User token | unique `(UserId, LoginProvider, Name)` | Default-token-provider state. |
| Tenant membership | unique `(TenantId, UserId)` | Tenant principal projection. |

The current EF schema also has a globally unique normalized username and globally unique normalized role name. That contradicts tenant-local uniqueness and makes the tenant composite indexes redundant. The Groundwork replacement should correct this rather than preserve the accidental global constraint.

Relationships are user-to-claims/logins/tokens/memberships, role-to-claims, and user-to-role many-to-many. Deleting a user or role currently cascades through EF foreign keys. Groundwork must either provide declared relationship/cascade operations or the adapter must perform the dependent deletes in one `IDocumentUnitOfWork`. Silent orphans are not acceptable.

### Identity Tenancy And Normalization

ASP.NET Core Identity's `FindByNameAsync` and `FindByEmailAsync` store contracts do not carry a tenant. The current service performs a global lookup and checks `TenantId` afterward. That is not storage-boundary tenant isolation and cannot support the same username in two tenants.

Before implementation, the Elsa slice must introduce an explicit tenant-aware adapter seam. Recommended direction:

- bind the selected login tenant to the Groundwork session/tenant context before calling `UserManager`;
- have every user/role store operation stamp or validate that tenant;
- use composite tenant-normalized indexes;
- require an explicit privileged session for cross-tenant administration and seeding;
- never fall back to a global lookup followed by an in-memory tenant check.

Identity normalization remains framework-owned: `ILookupNormalizer` produces normalized username/email/role values before store lookup. Persist both display and normalized values. Case-insensitive provider behavior must not be inferred from database collation.

### Identity Concurrency

`ConcurrencyStamp` on users and roles is an optimistic token. On update/delete, the store must compare the caller's stamp/version, rotate the stamp on success, and return `IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure())` on conflict. Lockout failure-count updates are a real concurrent hot path.

Groundwork's expected-version compare-and-swap model fits this. The adapter should keep the Groundwork envelope version as the authoritative compare-and-swap value and project a stable opaque representation into `ConcurrencyStamp`; it must not do an unconditional upsert. Multi-record changes to claims, roles, logins, and tokens use one unit of work.

## OpenIddict 7.5 Store Inventory

`UseEntityFrameworkCore` registers all four core store families even though Elsa's current first-party token service directly exercises only tokens. A replacement registered with OpenIddict core must either implement all four contracts or deliberately register a reduced custom resolver set and prove that unused manager paths fail at startup with a clear capability error. The safe compatibility target is all four.

Every OpenIddict EF entity carries an opaque `ConcurrencyToken`. All four stores must compare it on update/delete, rotate it after a successful update, and translate a stale Groundwork expected version into `OpenIddictExceptions.ConcurrencyException`. Their offset-list operations also require stable id ordering so page boundaries do not drift between providers.

### Application Store

`IOpenIddictApplicationStore<TApplication>` requires:

- CRUD and instantiation;
- count, offset list, and generic query/projection overloads;
- find by id and unique client id;
- find by redirect URI and post-logout redirect URI;
- getters/setters for application/client/consent types, client secret, display names, JSON web key set, permissions, redirect URIs, properties, requirements, and settings.

Indexes: unique `ClientId`; multi-value redirect and post-logout redirect URI lookup. Applications own authorizations and tokens logically.

### Authorization Store

`IOpenIddictAuthorizationStore<TAuthorization>` requires:

- CRUD, instantiation, count/list/generic query;
- find by id, application id, subject, and combined optional `(subject, client, status, type, scopes)` filters;
- creation date, application id, properties, scopes, status, subject, and type accessors;
- prune by creation threshold;
- bulk revoke by filters, application id, or subject.

Indexes must support application, status, subject, type, and the combined search shape. Scope containment needs a queryable representation rather than an opaque JSON string if those APIs are enabled.

### Scope Store

`IOpenIddictScopeStore<TScope>` requires:

- CRUD, instantiation, count/list/generic query;
- find by id, unique name, a set of names, and resource;
- localized descriptions/display names, properties, and resources.

Indexes: unique `Name`; multi-value `Resources` lookup.

### Token Store

`IOpenIddictTokenStore<TToken>` requires:

- CRUD, instantiation, count/list/generic query;
- find by id, unique reference id, application id, authorization id, subject, and combined optional `(subject, client, status, type)` filters;
- creation, expiration, and redemption dates;
- application/authorization ids, payload, properties, reference id, status, subject, and type;
- prune by threshold;
- bulk revoke by filters, application id, authorization id, or subject.

Indexes: unique `ReferenceId`; `AuthorizationId`; and a compound application/status/subject/type search index. The manager obfuscates a reference token before store lookup; the store persists and indexes only the obfuscated value.

### OpenIddict Operations Elsa Exercises Today

`OpenIddictTokenService` uses the token manager as follows:

| Flow | Required store behavior |
|---|---|
| Issue | Instantiate token, set subject/type/creation/expiration/status/reference/payload, create, get id. One access entry and one refresh entry are created. |
| Refresh | Find by obfuscated reference id; get status/expiration/subject/payload; atomically set redemption date/status through `TryRedeemAsync`; issue replacement entries. |
| Validate bearer | Local validation resolves the token-entry id embedded in the JWT and checks the persisted entry because token-entry validation is enabled. Revoked/redeemed/unknown entries fail closed. |
| Revoke refresh | Find by reference id, then atomically mark revoked. |
| Revoke access | Parse the private token id claim, find by id, then atomically mark revoked. |

Refresh redemption and revocation depend on optimistic concurrency. A concurrent replay must yield one successful redemption and one clean false/conflict result. The store must translate a stale Groundwork expected version into the OpenIddict concurrency exception expected by the manager; returning success after an overwrite would break single-use refresh tokens.

No application, authorization, or scope records are created by Elsa's first-party custom flow today. They remain part of the registered OpenIddict core compatibility surface and must be covered before claiming a general OpenIddict Groundwork package.

OpenIddict's store contracts contain no tenant parameter. The current EF entries are globally addressed; Elsa carries tenant identity in the access-token claims and refresh-token payload, not in the OpenIddict schema. The first Groundwork implementation should preserve that explicit tenant-agnostic storage-unit classification rather than pretending ambient tenant filtering is possible during token validation. If future hosts require tenant-partitioned token storage, the Elsa adapter must project `TokenIssueRequest.TenantId` into native metadata and define a privileged, claim-validated lookup path for bearer validation before enabling that mode.

### OpenIddict Relationships And Cleanup

The logical relationships are:

```text
Application -> Authorizations -> Tokens
Application ------------------> Tokens
Scope (independent, referenced by name in grants/authorizations)
```

EF currently uses nullable application and authorization foreign keys without delete cascade. OpenIddict's EF stores/managers perform explicit dependent revocation/deletion. Groundwork adapters must preserve that behavior atomically where multiple records change and must not rely on relational-only foreign keys.

Prune and revoke are scale-bearing server operations. Fetching all token/authorization documents and filtering in memory is forbidden. They require server-side date/range predicates and bulk mutation/delete support, with deterministic counts and cancellation.

## Groundwork Fit And Upstream Gaps

### Natural Document Fit

Use entity tables/collections containing canonical JSON plus native projected columns for users, roles, logins, memberships, OpenIddict applications, authorizations, scopes, and tokens. Small dependent records such as claims, user-role links, and user tokens may remain separate documents because they have independent lookup/uniqueness needs and participate in atomic updates.

Groundwork already provides the important base mechanics:

- canonical JSON envelopes;
- create-only and compare-and-swap expected versions;
- unique declared indexes;
- equality, `In`, case-insensitive `Contains`, ordering, offset paging, count, first, and any;
- relational and MongoDB document providers;
- explicit document units of work.

### Required Upstream Capabilities

| Gap | Blocking contract | Required capability |
|---|---|---|
| Compound indexes are declared but not queryable/physicalizable | Tenant-normalized Identity lookups; OpenIddict combined searches | Queryable, physicalizable compound indexes with uniqueness and deterministic field order. |
| No range comparisons | Token/authorization pruning; expiration queries | Portable date/number range operators with provider-native ordering/encoding. |
| No bulk mutation/delete by bounded predicate | OpenIddict prune/revoke | Specialized bounded bulk update/delete returning affected count, transactionally consistent. |
| Tenant filtering is query-only | Every tenant-aware Identity read/write/delete; document-by-id operations | Storage-boundary tenant stamp and validation on load/save/delete/query, with explicit privileged bypass. OpenIddict is explicitly tenant-agnostic until a claim-validated partition design exists. |
| No relationship/cascade declaration | Identity dependent records; OpenIddict lifecycle | Either a small provider-neutral relationship/cascade primitive or a documented adapter-owned UoW pattern with conformance tests. |
| Generic `IQueryable` delegates in OpenIddict | `CountAsync<TResult>`, `GetAsync`, `ListAsync<TState,TResult>` | Do not expose general LINQ from Groundwork. Add an OpenIddict-specific bounded expression translator for supported manager shapes, or fail these optional generic manager methods explicitly. Never client-evaluate unbounded data. |
| Single-field string-normalized index model | Dates, booleans, compound uniqueness, multi-value redirect/resource/scope fields | Native typed and multi-value projected columns/indexes through `PhysicalTableDefinition`. |
| Capability claims are not yet identity-specific | Four-provider support claims | Executable provider conformance for every operation below, including MongoDB transaction deployment requirements. |

The `IQueryable` overloads are the only fundamental contract mismatch. They are extension points for consumers to supply arbitrary projections, not operations Elsa currently invokes. The recommended first release implements every named OpenIddict operation server-side, deliberately rejects generic query delegates with a documented capability error, and does not advertise a general-purpose OpenIddict store until a bounded translator exists. This keeps the agreed no-general-`IQueryable` rule intact.

## Required Four-Provider Conformance

Run the same black-box suite against SQLite, SQL Server, PostgreSQL, and MongoDB. MongoDB tests that require multi-document transactions must use a replica set or sharded deployment.

### Identity Store Contract Suite

- CRUD and normalized lookup for users/roles.
- Same normalized username/role may exist in different tenants; duplicates in one tenant fail.
- Email lookup is tenant-scoped and non-unique as configured.
- Wrong-tenant load/update/delete is indistinguishable from not found and writes nothing.
- User/role concurrency conflict returns the framework's concurrency failure.
- Concurrent lockout failures do not lose increments.
- Claims, roles, external logins, tokens, and tenant memberships round-trip with uniqueness.
- User/role deletion produces no orphaned dependents.
- Admin seed is idempotent under concurrent startup and logs no production password.
- Username/email sign-in, wrong password, unknown user timing path, lockout, cookie issuance, and claims projection work through the highest public seam.
- Default token providers that remain advertised have focused create/validate/redeem tests.
- Unsupported queryable/passkey capabilities are not registered.

### OpenIddict Store Contract Suite

- Full CRUD/getter/setter round trips for application, authorization, scope, and token descriptors.
- Unique client id, scope name, and obfuscated reference id enforcement.
- Every named find/filter/list/count/order/page operation executes server-side with equivalent results and stable id ordering.
- Redirect URI, resource, and scope multi-value lookups are exact and deterministic.
- Stale update/delete throws the concurrency exception expected by OpenIddict managers.
- Two concurrent refresh redeemers produce exactly one success.
- Revoked/redeemed/expired/unknown access and refresh tokens fail validation.
- Application/authorization/token relationship operations leave no invalid dangling state.
- Prune and bulk revoke return exact counts and are restart/failure safe.
- Cancellation and transaction rollback leave no partial mutation.
- Generic query overloads either pass a defined translator suite or fail immediately with a stable capability exception; never load all records.

### Highest-Seam Integration Suite

1. Compose the real CShells features and Groundwork provider, not store classes directly.
2. Apply/validate the Groundwork manifest through the same startup/CLI path as production.
3. Seed an admin, submit the login endpoint, receive the Elsa cookie, and call a permission-protected API.
4. Issue access/refresh tokens through `ITokenService`, call a bearer-protected endpoint, rotate the refresh token, prove replay rejection, revoke both token kinds, and prove immediate rejection.
5. Repeat restart tests against durable provider containers and verify state, schema history, and indexes survive.

## Package And Host Changes For The Implementation Slice

Add concrete packages with no Groundwork references from core contracts, for example:

- `Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork`;
- `Elsa.Foundation.Identity.OpenIddict.Groundwork` (or a Groundwork-owned OpenIddict adapter package consumed by Elsa).

Then:

1. replace the `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` feature with a Groundwork feature and provider-agnostic connection/storage-unit settings;
2. change `AddIdentityCoreServices` registration to explicit Groundwork user/role stores;
3. evolve or retire `IdentityGroundworkPersistenceFeature` so the four Elsa IAM abstractions adapt the same framework identity documents/UoW, with no duplicate registrations or parallel user/role document authority;
4. replace `UseEntityFrameworkCore` with OpenIddict core store/resolver registrations;
5. remove both `DbContext` types, initial migrations, factories, EF initializers, and EF-only tests after parity passes;
6. remove `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `OpenIddict.EntityFrameworkCore`, and EF package/project references from identity projects, the reference host, tests, and central package versions when no other lane needs them;
7. rename shell settings and production composition without preserving EF-specific feature aliases in this greenfield product;
8. let Groundwork's host naming policy select physical names while feature packages provide stable logical storage-unit ids and defaults;
9. keep ASP.NET Core Identity/OpenIddict types and packages entirely in concrete foundation packages; Elsa identity abstractions remain unchanged.

## Dependency-Ordered Follow-Up Work

1. Groundwork compound/typed/multi-value indexes and range queries.
2. Groundwork storage-boundary tenancy and privileged sessions.
3. Groundwork bounded bulk update/delete and four-provider UoW/OCC conformance.
4. Decide and test the OpenIddict generic-query capability boundary.
5. Reconcile `IdentityGroundworkPersistenceFeature` and its manifest with the single-authority framework-document design.
6. Elsa tenant-aware Identity manager/store seam.
7. Groundwork-backed ASP.NET Core Identity stores plus provider conformance.
8. Groundwork-backed OpenIddict stores plus provider conformance.
9. Reference-host switch, authentication/authorization integration suite, then EF identity/OpenIddict deletion.

The production-store slices must not begin by copying the current EF schema mechanically. They should use the accepted Groundwork physical-storage model, explicit tenant ownership, canonical JSON, and only the native columns/indexes proven necessary by these contracts.

## Source Pointers

- [Identity EF registration](../../src/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/Extensions/AspNetCoreIdentityEntityFrameworkCoreServiceCollectionExtensions.cs)
- [Existing Groundwork IAM persistence feature](../../src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityGroundworkPersistenceFeature.cs)
- [Existing Groundwork IAM storage manifest](../../src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityStorageManifest.cs)
- [Identity framework registration](../../src/Elsa/Foundation/Identity/AspNetCoreIdentity/Extensions/AspNetCoreIdentityServiceCollectionExtensions.cs)
- [Identity model](../../src/Elsa/Foundation/Identity/AspNetCoreIdentity/Models/AspNetCoreIdentityUser.cs)
- [Identity EF model](../../src/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/ApplicationIdentityDbContext.cs)
- [Identity seeder](../../src/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/Seeding/IdentitySeeder.cs)
- [Identity sign-in flow](../../src/Elsa/Foundation/Identity/AspNetCoreIdentity/Services/AspNetCoreIdentitySignInService.cs)
- [OpenIddict registration](../../src/Elsa/Foundation/Identity/OpenIddict/Extensions/OpenIddictIdentityServiceCollectionExtensions.cs)
- [OpenIddict token flow](../../src/Elsa/Foundation/Identity/OpenIddict/OpenIddictTokenService.cs)
- [Workbench-owned OpenIddict vendor EF model](../../src/Apps/Elsa.Workbench/OpenIddict/OpenIddictIdentityDbContext.cs)
- [Identity tests](../../tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity)
- [OpenIddict tests](../../tests/Elsa/Foundation/Identity/Tests/OpenIddict)

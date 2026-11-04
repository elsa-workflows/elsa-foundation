# Identity EF Core persistence

This module contains opt-in EF Core implementations for the complete Foundation Identity IAM authority:
users, roles, claim mappings, external identities, tenant memberships, applications, credentials, and the
relationship, reservation, and mutation-receipt rows needed to preserve their contracts. The separate
provider-configuration feature owns `IProviderConfigurationStore` and
`IRevisionAwareProviderConfigurationStore` in its own EF context.

The sibling `Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore` module adapts ASP.NET Core
Identity's complete user, role, claim, login, role-membership, token, authenticator, recovery-code,
authentication, session-invalidation, and seeding surfaces to this same IAM authority. It does not create an
`IdentityDbContext` or a second Identity schema.

## Selection and conflicts

Select `IdentityProviderConfigurationEntityFrameworkCore`, `IdentityIamEntityFrameworkCore`, and, when
needed, `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore`, or call their matching registration
extensions, with exactly one of SQLite, SQL Server, PostgreSQL, or MySQL. The host must reference the matching
provider package. Equivalent repeated registrations are idempotent; a
different provider, connection string, or connection name fails before registrations are partially changed.
Direct host registrations for a selected feature's replacement contracts conflict with explicit EF selection
and fail instead of being silently removed or winning by registration order. Startup validation also rejects
a registration added after a feature. Selection is order-independent, and each authority has exactly
one owner.

The module preserves tenant/global access checks, tenant-first effective fallback for provider configurations,
lossless record round trips, bounded and stably ordered queries, uniqueness reservations, atomic aggregate and
relationship writes, replay receipts, and optimistic compare-and-swap revisions.

## Schema and migrations

`IdentityIamDbContext` and `IdentityProviderConfigurationDbContext` each ship migrations for SQLite, SQL
Server, PostgreSQL, and MySQL under `Migrations/`, recorded in their own `__EFMigrationsHistory_ElsaIdentityIam`
and `__EFMigrationsHistory_ElsaIdentityProviderConfiguration` tables. Each feature registers
`AddEfModuleMigrations<TContext>(Provider)`, so the shared `EfModuleMigrator<TContext>` applies or validates
them in the CShells Prepare phase according to `EfMigrateOptions.Policy`.

OpenIddict remains in its separate vendor-owned context. This module does not reference, configure, or merge
`OpenIddictIdentityDbContext`.

## Extension points

- Contract ownership and replacement semantics: [Foundation Identity abstractions](../../Core/EXTENSION_POINTS.md).
- ASP.NET Core Identity adapter composition and framework surfaces: [ASP.NET Core Identity EF adapter](../../AspNetCoreIdentity/EntityFrameworkCore/EXTENSION_POINTS.md).

Issuer-qualified login migration `IssuerQualifiedExternalLogins` is additive for all four providers and preserves the
original Initial histories. Existing login rows keep a null issuer; no issuer is guessed from a connection's current
configuration. IAM schema family 1.1.0 reads 1.0.0 through a content-preserving upcaster. Qualified keys hash exact
issuer/subject values separately from legacy keys, while retaining the same authoritative user registry, CAS and
aggregate-delete boundary. The framework `UserLoginInfo` adapter lists only issuer-less links because it cannot
represent an issuer; managed link inspection uses the provider-neutral external-identity store.

Atomic external admission nests the existing user/relationship mutations under one outer IAM transaction and
acknowledgement. New users receive a security stamp and no local password. Missing default roles, stale matched
user snapshots, disabled users and conflicting identifiers refuse admission; linking an existing user preserves
saved roles. Its regression suite injects a link-write failure and races independent SQLite contexts.

## Coordinated provider and IAM revisions

The revision-aware user/role stores can return opaque `IamRevisionGuard` snapshots. User guards bind the account
instance and revision; role guards bind definition contents and revision. Capturing them never initializes an
account epoch. Missing accounts, unsupported authorities and older accounts without an epoch return no guard.

`IRevisionAwareProviderConfigurationStore.SupportsIdentityRevisionGuards` reports whether the selected EF IAM
and provider contexts target the same relational provider and exact connection string. When available,
`SaveWithIdentityRevisionGuardsAsync` CAS-saves a global provider configuration and advances the guarded IAM
revisions in one fresh serializable physical transaction. It preserves account security stamps, permissions and
readable payload schema. A recreated root cannot reuse an older guard at the same numeric revision. Stale IAM
or provider state, provider-save failure and unsupported schema never commit a partial unit. The operation
requires privileged global access and refuses caller transactions, ambient transactions and pending caller changes.
Supplied connection objects and runtime-retargeted contexts do not qualify: fresh contexts must provably own the
configured connection-string target before opening it. Lost commit acknowledgement stays an unknown outcome,
requiring a current-state read rather than blind replay or a plain-conflict report.
The existing two-argument store constructor and ordinary saves remain available.

This is the storage operation for managed-authentication recovery coordination. Managed catalog-writer and
qualified-unlink integration remain tracked by spec 1205 T024; the new storage operation alone does not provide
those product-level guarantees. Split-target or custom authorities must supply proven atomic coordination
before their revision guards can authorize a managed destructive change.

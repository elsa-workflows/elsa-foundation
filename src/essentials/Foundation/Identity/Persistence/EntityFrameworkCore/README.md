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

- Contract ownership and replacement semantics: [Foundation Identity abstractions](../../Abstractions/EXTENSION_POINTS.md).
- ASP.NET Core Identity adapter composition and framework surfaces: [ASP.NET Core Identity EF adapter](../../AspNetCoreIdentity/EntityFrameworkCore/EXTENSION_POINTS.md).
- Known persistence implementations and backend composition: [Foundation Identity persistence](../EntityFrameworkCore/EXTENSION_POINTS.md).

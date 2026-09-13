# Identity EF Core persistence

This module contains opt-in EF Core implementations for provider configurations, applications, and
credentials. The application and credential feature owns `IApplicationStore`,
`IRevisionAwareApplicationStore`, `ICredentialStore`, and `IRevisionAwareCredentialStore`; the separate
provider-configuration feature owns `IProviderConfigurationStore` and
`IRevisionAwareProviderConfigurationStore`. Groundwork remains the default for every other Identity store
until its separate replacement gate is complete.

## Selection and conflicts

Select `IdentityProviderConfigurationEntityFrameworkCore` and/or `IdentityIamEntityFrameworkCore`, or call
their matching registration extensions, with exactly one of SQLite, SQL Server, PostgreSQL, or MySQL. The
host must reference the matching provider package. Equivalent repeated registrations are idempotent; a
different provider, connection string, or connection name fails before registrations are partially changed.
Direct host registrations for a selected feature's replacement contracts conflict with explicit EF selection
and fail instead of being silently removed or winning by registration order. Startup validation also rejects
a registration added after a feature. Selection is order-independent with the Groundwork Identity feature:
each EF feature replaces only its owned contracts and Groundwork keeps unrelated stores.

The module preserves tenant/global access checks, tenant-first effective fallback for provider configurations,
lossless record round trips, unconditional upsert, and optimistic compare-and-swap revisions. Provider-specific
migrations, runtime schema initialization, default host selection, and Groundwork deletion are later rollout
gates.

OpenIddict remains in its separate vendor-owned context. This module does not reference, configure, or merge
`OpenIddictIdentityDbContext`.

## Extension points

- Contract ownership and replacement semantics: [Foundation Identity abstractions](../../Abstractions/EXTENSION_POINTS.md).
- Known persistence implementations and backend composition: [Foundation Identity persistence](../Groundwork/EXTENSION_POINTS.md).

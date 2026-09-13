# Identity provider-configuration EF Core persistence

This module is the opt-in EF Core implementation of `IProviderConfigurationStore` and
`IRevisionAwareProviderConfigurationStore`. It owns only the tenant and global provider-configuration
units; Groundwork remains the default for every other Identity store until their separate replacement gates
are complete.

## Selection and conflicts

Select the `IdentityProviderConfigurationEntityFrameworkCore` shell feature, or call
`AddIdentityProviderConfigurationEntityFrameworkCore`, with exactly one of SQLite, SQL Server, PostgreSQL,
or MySQL. The host must reference the matching provider package. Equivalent repeated registrations are
idempotent; a different provider, connection string, or connection name fails before registrations are
partially changed. Selection is order-independent when the Groundwork Identity feature is also composed: EF
replaces only the two provider-configuration contracts and Groundwork keeps all unrelated stores.

The module preserves tenant/global access checks, tenant-first effective fallback, lossless configuration
round trips, unconditional upsert, and optimistic compare-and-swap revisions. Provider-specific migrations,
runtime schema initialization, default host selection, and Groundwork deletion are later rollout gates.

OpenIddict remains in its separate vendor-owned context. This module does not reference, configure, or merge
`OpenIddictIdentityDbContext`.

## Extension points

- Contract ownership and replacement semantics: [Foundation Identity abstractions](../../Abstractions/EXTENSION_POINTS.md).
- Known persistence implementations and backend composition: [Foundation Identity persistence](../Groundwork/EXTENSION_POINTS.md).

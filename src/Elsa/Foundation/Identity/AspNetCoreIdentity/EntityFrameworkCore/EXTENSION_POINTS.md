# Extension points - Foundation ASP.NET Core Identity EF adapter

This module is the opt-in ASP.NET Core Identity adapter over the shared Foundation Identity EF authority. It owns no parallel `IdentityDbContext`: every framework operation is translated to the same user, role, relationship, reservation, token, membership, and mutation-receipt rows used by the provider-neutral IAM stores.

## Authority selection

Select `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore`, or call `AddFoundationAspNetCoreIdentityEntityFrameworkCore(...)`, to bind the framework surface to `EfCoreIdentityUserStore` and `EfCoreIdentityRoleStore`. The registration composes `IdentityIamEntityFrameworkCore` with the same provider and connection and rejects a competing ASP.NET Identity or IAM authority before leaving descriptors behind. Equivalent repeated registrations are idempotent.

OpenIddict remains in its separate vendor-owned EF context.

## Adapted framework contracts

| Framework surface | EF adapter | Preserved behavior |
|---|---|---|
| `IUserStore`, password, security-stamp, email, lockout, phone, and two-factor user stores | `EfCoreIdentityUserStore` | User aggregate CAS, tenant isolation, normalized uniqueness, lossless framework fields, and typed Identity errors. |
| Login, claim, role-membership, authentication-token, authenticator-key, and recovery-code user stores | `EfCoreIdentityUserStore` | Atomic relationship ownership, bounded materialization, login display-name round trip, idempotent adds, and single-use recovery codes. |
| `IRoleStore` and `IRoleClaimStore` | `EfCoreIdentityRoleStore` | Role aggregate CAS, normalized uniqueness, stable membership lookup, and atomic claim changes. |
| `IUserClaimsPrincipalFactory` | `EfCoreIdentityClaimsPrincipalFactory` | Claims projection from the authoritative EF-backed user and role state. |
| `IAuthenticationSessionInvalidator` and cookie events | `EfCoreIdentitySessionInvalidator`, `EfCoreIdentityCookieEvents` | Security-stamp rotation and tenant-bound session rejection. |
| `IShellInitializer` and hosted seeding | `EfCoreIdentitySeeder` | Redacted, idempotent administrator convergence after schema initialization. |

These are single-authority adapter bindings, not additive contributor seams. Hosts should replace the provider-neutral IAM contracts through the persistence feature rather than registering individual framework stores beside this adapter.

## Constitutional basis

- Framework section 2.6.2 - single active replacement contracts.
- Framework section 2.22.1 - owning per-domain extension-point catalog.
- Framework section 2.23 - registration and implementation test obligations.

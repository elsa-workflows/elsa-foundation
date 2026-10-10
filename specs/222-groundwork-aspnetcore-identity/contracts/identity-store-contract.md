# Contract: Groundwork ASP.NET Core Identity Stores

## Boundary

This contract defines the observable behavior of issue #644. ASP.NET Core Identity managers and Elsa IAM ports are two adapters over one Foundation Identity Groundwork authority. The feature does not expose Groundwork types through Elsa core contracts.

## Required Framework Capability Denominator

This denominator was reconciled against the .NET 10 `Microsoft.Extensions.Identity.Core` reference contract: 19 user/role store interfaces, 77 interface-declared members, and the inherited `IDisposable.Dispose()` lifecycle member. Member lists below are the members declared by each interface; inherited base-store members are listed once under `IUserStore<TUser>` or `IRoleStore<TRole>`. None of these interfaces overloads a listed member name, so interface plus member name is the exact method identity. The concrete stores also satisfy `Dispose()`.

### Required User Interfaces And Methods

The registered user store implements every member in this table for `AspNetCoreIdentityUser` without client evaluation or a method that throws `NotSupportedException` after capability discovery.

| Interface | Declared .NET 10 members |
|---|---|
| `IUserStore<TUser>` | `GetUserIdAsync`, `GetUserNameAsync`, `SetUserNameAsync`, `GetNormalizedUserNameAsync`, `SetNormalizedUserNameAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `FindByIdAsync`, `FindByNameAsync` |
| `IUserPasswordStore<TUser>` | `SetPasswordHashAsync`, `GetPasswordHashAsync`, `HasPasswordAsync` |
| `IUserSecurityStampStore<TUser>` | `SetSecurityStampAsync`, `GetSecurityStampAsync` |
| `IUserEmailStore<TUser>` | `SetEmailAsync`, `GetEmailAsync`, `GetEmailConfirmedAsync`, `SetEmailConfirmedAsync`, `FindByEmailAsync`, `GetNormalizedEmailAsync`, `SetNormalizedEmailAsync` |
| `IUserLockoutStore<TUser>` | `GetLockoutEndDateAsync`, `SetLockoutEndDateAsync`, `IncrementAccessFailedCountAsync`, `ResetAccessFailedCountAsync`, `GetAccessFailedCountAsync`, `GetLockoutEnabledAsync`, `SetLockoutEnabledAsync` |
| `IUserPhoneNumberStore<TUser>` | `SetPhoneNumberAsync`, `GetPhoneNumberAsync`, `GetPhoneNumberConfirmedAsync`, `SetPhoneNumberConfirmedAsync` |
| `IUserTwoFactorStore<TUser>` | `SetTwoFactorEnabledAsync`, `GetTwoFactorEnabledAsync` |
| `IUserLoginStore<TUser>` | `AddLoginAsync`, `RemoveLoginAsync`, `GetLoginsAsync`, `FindByLoginAsync` |
| `IUserClaimStore<TUser>` | `GetClaimsAsync`, `AddClaimsAsync`, `ReplaceClaimAsync`, `RemoveClaimsAsync`, `GetUsersForClaimAsync` |
| `IUserRoleStore<TUser>` | `AddToRoleAsync`, `RemoveFromRoleAsync`, `GetRolesAsync`, `IsInRoleAsync`, `GetUsersInRoleAsync` |
| `IUserAuthenticationTokenStore<TUser>` | `SetTokenAsync`, `RemoveTokenAsync`, `GetTokenAsync` |
| `IUserAuthenticatorKeyStore<TUser>` | `SetAuthenticatorKeyAsync`, `GetAuthenticatorKeyAsync` |
| `IUserTwoFactorRecoveryCodeStore<TUser>` | `ReplaceCodesAsync`, `RedeemCodeAsync`, `CountCodesAsync` |

### Required Role Interfaces And Methods

The registered role store implements every member in this table for framework `IdentityRole`.

| Interface | Declared .NET 10 members |
|---|---|
| `IRoleStore<TRole>` | `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `GetRoleIdAsync`, `GetRoleNameAsync`, `SetRoleNameAsync`, `GetNormalizedRoleNameAsync`, `SetNormalizedRoleNameAsync`, `FindByIdAsync`, `FindByNameAsync` |
| `IRoleClaimStore<TRole>` | `GetClaimsAsync`, `AddClaimAsync`, `RemoveClaimAsync` |

### Explicitly Unsupported .NET 10 Interfaces

Capability discovery must report these interfaces absent. Their complete additional member denominator is recorded so a later implementation cannot accidentally register a partial capability.

| Interface | Additional .NET 10 member | #644 decision |
|---|---|---|
| `IQueryableUserStore<TUser>` | `Users` | Not implemented: arbitrary `IQueryable` conflicts with bounded native routes. |
| `IQueryableRoleStore<TRole>` | `Roles` | Not implemented: arbitrary `IQueryable` conflicts with bounded native routes. |
| `IUserPasskeyStore<TUser>` | `AddOrUpdatePasskeyAsync`, `GetPasskeysAsync`, `FindByPasskeyIdAsync`, `FindPasskeyAsync`, `RemovePasskeyAsync` | Not implemented: passkeys are outside the required workload and no partial interface is advertised. |
| `IProtectedUserStore<TUser>` | Marker only; no additional members | Not implemented: the store does not claim `StoreOptions.ProtectPersonalData` support. |

### Elsa IAM Adapters

The feature registers one scoped implementation of each existing provider-neutral port:

- `Elsa.Foundation.Identity.Abstractions.Iam.IUserStore`;
- `IRoleStore`;
- `IExternalIdentityStore`;
- `ITenantMembershipStore`.

These adapters read and mutate the same authority documents as the framework stores and preserve all Elsa-owned fields.

The current operation denominator is exact and remains Groundwork-free:

| Port | Operation | Required authority behavior |
|---|---|---|
| `IUserStore` | `FindAsync(string tenantId, string userId, CancellationToken)` | Exact scoped user lookup; return `null` without disclosure when absent or outside scope. |
| `IUserStore` | `FindByEmailAsync(string tenantId, string email, CancellationToken)` | Tenant-local email lookup preserving the existing case-insensitive round-trip objective; fail closed on ambiguity. |
| `IUserStore` | `SaveAsync(UserRecord user, CancellationToken)` | Preserve user name, email, display name, status, ownership, role IDs, and direct permissions; Groundwork uses the additive revision/conflict path rather than unconditional overwrite. |
| `IRoleStore` | `FindAsync(string tenantId, string roleId, CancellationToken)` | Exact scoped role lookup. |
| `IRoleStore` | `ListAsync(string tenantId, CancellationToken)` | Tenant-local deterministic bounded role list preserving every role field. |
| `IRoleStore` | `SaveAsync(RoleRecord role, CancellationToken)` | Preserve name, description, permissions, and system-role state through the additive revision/conflict path. |
| `IExternalIdentityStore` | `FindBySubjectAsync(string tenantId, string provider, string providerSubject, CancellationToken)` | Exact scoped provider-subject lookup. |
| `IExternalIdentityStore` | `ListForUserAsync(string tenantId, string userId, CancellationToken)` | Deterministic bounded list for one scoped user. |
| `IExternalIdentityStore` | `SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken)` | Preserve provider, subject, user, linked/last-seen timestamps, and link policy through the additive conflict path. |
| `ITenantMembershipStore` | `FindAsync(string tenantId, string userId, CancellationToken)` | Exact scoped membership lookup. |
| `ITenantMembershipStore` | `SaveAsync(TenantMembershipRecord membership, CancellationToken)` | Preserve status, role IDs, and direct permissions through the additive revision/conflict path. |

`IApplicationStore`, `ICredentialStore`, `IClaimMappingStore`, and `IProviderConfigurationStore` are separate IAM persistence lanes and are not adapters over the ASP.NET Core Identity user/role authority in #644.

## Scope Contract

- Every store instance is scoped.
- Before its first provider operation, the store acquires an immutable ordinary `PersistenceAccessContext` with one nonblank scope.
- Entity-carried tenant identity must match that scope.
- Framework methods lacking a tenant parameter use only the already-bound current scope.
- Password sign-in binds the effective request/default tenant before normalized lookup.
- Seeding uses an explicit privileged-scoped purpose; it never receives cross-scope authority by omission.
- Wrong-scope identity behaves as not found to ordinary callers and never reports the actual owner scope.

## Result And Conflict Contract

| Operation | Success | Duplicate/conflict | Stale revision | Missing |
|---|---|---|---|---|
| User create | `IdentityResult.Success` and version-backed stamp | `DuplicateUserName`; `DuplicateEmail` when policy enabled | N/A | N/A |
| User update/delete | Success and rotated stamp / deletion | Logical identity collision mapped precisely | `ConcurrencyFailure` | Framework-compatible failure/no disclosure |
| Role create | Success and version-backed stamp | `DuplicateRoleName` | N/A | N/A |
| Role update/delete | Success and rotated stamp / deletion | `DuplicateRoleName` | `ConcurrencyFailure` | Framework-compatible failure/no disclosure |
| Login add | Link present exactly once | Stable login-already-associated failure | Owner concurrency failure | User not found/non-disclosing |
| Role link add | Link present on both owners | Idempotent existing association | User or role concurrency failure | User/role not found |
| Token set | Exact deterministic token replaced conditionally | N/A | User concurrency failure | User not found |

Provider exception types do not escape. Cancellation is never wrapped. Commit uncertainty is reconciled when exact evidence proves the result; otherwise a Foundation Identity-scoped uncertain-commit exception preserves the original cause.

## Normalization And Lookup Contract

- Framework `ILookupNormalizer` owns normalization.
- Username and role lookups are exact within the current scope.
- Email lookup requests at most two exact normalized matches. Multiple matches return no sign-in candidate when unique email is disabled.
- External-login lookup is exact on current scope + provider + provider key.
- Users-for-claim and users-in-role return deterministic ID order and bounded pages internally; the framework list result is assembled only from that declared route.
- Unsupported arbitrary query shapes fail before provider I/O.

## Relationship And Delete Contract

- A relationship mutation commits its scalar link and every affected owner registry in one unit of work.
- A dependent delete enumerates child IDs from the CAS-protected registry, never from a pre-transaction query.
- User deletion removes claims, logins, role links, tokens, memberships, and email reservation atomically.
- Role deletion removes role claims and user-role links atomically and updates each linked user registry.
- Lost acknowledgement uses deterministic link IDs and registry evidence to classify the committed state.

## Seeder Contract

- Missing/partial username/password configuration fails before provider writes.
- Schema readiness is a prerequisite; the seeder never applies schema.
- Role/user creation and relationship writes are create-only or conditional and converge under concurrent instances.
- The seeded role contains wildcard `*` plus every current permission-catalog key.
- Configured password policy is honored through `UserManager`.
- Production logs never contain password or token values. Development password logging remains limited to the explicitly marked development seed.
- The same coordinator instance is exposed through both host and CShells lifecycle hooks.

## Composition Contract

- `FoundationIdentityAspNetCoreIdentityGroundwork` is an explicit feature.
- It requires the provider-neutral ASP.NET Core Identity feature and one selected Groundwork substrate.
- It does not own connection strings or provider selection.
- Groundwork plus EF Identity in one service collection is a startup error naming both features.
- Registering a second user/role authority or duplicate external-login document kind is a startup/architecture error.
- The temporary EF feature remains separately selectable only for #646 and receives no new behavior.

## Highest-Seam Acceptance

One production-shaped scenario must prove:

1. concurrent admin initialization;
2. username and unique-email login;
3. indistinguishable bad-password, unknown-user, ambiguous-email, and wrong-tenant failure;
4. lockout after configured failures and rejection of a correct password while locked;
5. cookie issuance;
6. subject, tenant, role, direct-permission, and role-permission claims;
7. access to a permission-protected endpoint;
8. logout/session invalidation behavior already owned by the host;
9. persistence after dispose/reopen and child-process restart.

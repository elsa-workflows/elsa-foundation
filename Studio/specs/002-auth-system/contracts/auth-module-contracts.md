# Contract: Foundation Auth Module Contracts

This contract defines the host-facing module seams in `elsa-foundation`. They are
described language-neutrally but target C#/.NET and the existing CShells feature
model. Reference-stack implementations (ASP.NET Core Identity, OpenIddict, ASP.NET
Core OIDC handlers) MUST sit behind these contracts so feature modules never bind
to a provider.

## Composition rules

- Auth is enabled by composing modules/features; the host MUST NOT call
  provider-specific registration directly.
- Exactly one provider MAY be marked default challenge provider; multiple
  providers MAY be enabled concurrently.
- Enabling an authentication provider MUST NOT require changes to feature modules
  or Studio screens.
- A module MUST declare the capabilities it provides so the host can compute
  effective capabilities and detect invalid combinations at startup.

## Authentication plane

### `IAuthenticationProviderModule`

Registers an authentication provider into the host.

- `Id` / `Kind` / `DisplayName`.
- `Capabilities`: `ProviderCapabilities`.
- `ConfigureServices(services, settings)`: registers ASP.NET Core auth
  scheme(s)/handlers and provider services.
- `ValidateSettings(settings)`: returns actionable diagnostics (bad authority,
  missing redirect URI, missing scope, weak key) — surfaced at startup and in the
  provider management API.

### `IAuthenticationProviderManager`

Runtime catalog and routing.

- `GetProviders(tenant)`: enabled providers for a tenant.
- `GetDefault(tenant)`: default challenge provider.
- `Challenge(context, providerId)`: begins sign-in for a provider.
- `SignOut(context)`: ends the session (and provider session where supported).

### `IPrincipalFactory`

Normalizes a provider principal into the internal claims/permission shape.

- `CreatePrincipalAsync(externalPrincipal, provider, tenant)`: resolves/links the
  `User`, applies `ClaimMappingRule`s, and attaches the `EffectivePermissionSet`
  as claims.

### `ITokenService` (issuance-capable providers only)

- `IssueAsync(subject, scopes, tenant)`: issue access (+ refresh) tokens.
- `RefreshAsync(refreshToken)`: refresh.
- `IntrospectAsync(token)` / `RevokeAsync(token)`: where the provider supports it.

Providers advertise support via `ProviderCapabilities`; callers MUST check
capabilities rather than assume.

## IAM plane

### Stores (swappable persistence)

- `IUserStore`, `IRoleStore`, `IPermissionCatalog`, `IApplicationStore`,
  `ICredentialStore`, `IExternalIdentityStore`, `IClaimMappingStore`,
  `IProviderConfigurationStore`, `ITenantMembershipStore`.

Each store is independently replaceable. The reference stack binds users/roles to
ASP.NET Core Identity stores and applications/credentials/scopes to OpenIddict
stores; alternative stores (including a legacy Elsa Identity adapter) MUST satisfy
the same contracts.

### Managers (domain operations)

- `IUserManager`, `IRoleManager`, `IApplicationManager`, `ICredentialManager`,
  `IProviderManager`, `IClaimMappingManager`.

Managers enforce invariants (e.g. lockout-prevention safeguards, rotation grace
windows, system-role protection) independent of the underlying store.

### `IIdentityBootstrap`

Optional, idempotent initial setup (seed admin role/user, default permissions).
MUST reject weak/dev secrets outside development/demo environments.

## Authorization plane

### `IPermissionCatalog`

The shared catalog of `Permission` keys. Modules MAY contribute permissions at
startup; keys MUST be stable and namespaced.

### Policy integration

- Authorization is expressed as `RequirePermission("<key>")` over ASP.NET Core
  authorization policies/requirements.
- `IPermissionEvaluator` resolves whether a principal holds a permission in the
  active tenant, sourced from claims and/or server-side `EffectivePermissionSet`.
- Resource-scoped checks use authorization handlers keyed by permission +
  resource (e.g. tenant/resource ownership).

### Claims normalization contract

- A principal exposes a documented, stable claim set:
  `sub`, `name`, `tenant_id`, `role` (multi), and `permission` (multi) claims.
- The `IPrincipalFactory` + mapping pipeline is the only place provider-specific
  claims are translated; downstream code consumes only normalized claims.
- Permission propagation boundary MUST be documented per provider: whether a
  permission change is reflected immediately (server-side evaluation) or at next
  token refresh (claim-embedded).

## Ownership-mode contract

### `IOwnershipModeProvider`

- `GetMode(tenant)`: returns `OwnershipMode`.
- `GetEffectiveCapabilities(tenant)`: combines mode with active provider
  capabilities into the gates Studio consumes.

Rules:

- In `external-owned`, local user/role/application creation **and updates** MUST be
  disabled or read-only; the system MUST NOT silently write local records the IdP
  owns. Role writes are gated by `roleAuthority` / `supportsLocalRoleManagement`
  exactly as user writes are gated by `userAuthority`, so an external-owned
  deployment cannot leave local role mutation enabled and drift from the IdP.
- In `hybrid`, identity lifecycle is external while roles/permissions/apps may be
  foundation-owned; the contract MUST make each authority (`userAuthority`,
  `roleAuthority`, `applicationAuthority`) explicit, and every management write
  MUST check the authority for the entity it mutates.
- Effective capabilities MUST be discoverable via the management API so Studio can
  render correct affordances without hard-coded mode logic.

## Multi-tenancy contract

- Every store and manager operation MUST be tenant-aware.
- Provider configuration, mapping rules, users, roles, and applications MUST be
  isolated per tenant.
- The active tenant MUST be resolvable from the principal (tenant claim) and/or
  request context, consistent with existing Elsa tenant resolution.

## Security defaults (normative)

- Reject missing/short/known-default signing keys outside development/demo.
- Require HTTPS metadata for external providers in production.
- Hash all secrets at rest (client secrets, API keys) with a strong KDF.
- Never log or return tokens/secrets in diagnostics or audit detail.
- Emit `AuthEvent`s for authentication, authorization denials, and identity/credential changes.

## Capability/ownership matrix (informative)

| Mode | Local users | Local roles | Local apps | Claim mapping |
|---|---|---|---|---|
| foundation-owned (default) | enabled | enabled | enabled | optional |
| external-owned | read-only/disabled | read-only/disabled | read-only/disabled | required |
| hybrid | per provider | enabled | enabled | typical |

Actual cell values are computed from `OwnershipMode` + `ProviderCapabilities`, not
hard-coded.

# Data Model: Elsa Foundation Auth System

This model describes the provider-agnostic IAM domain plus the configuration
entities that drive authentication, authorization, and ownership behavior. It is
deliberately implementation-neutral; the reference stack maps these onto ASP.NET
Core Identity (users/roles) and OpenIddict (applications/scopes/tokens) where
applicable.

## IAM Domain

### User

The canonical internal principal. Authoritative when Elsa owns IAM; a linked
shadow record when an external IdP owns identity.

- `id`: Stable internal identifier.
- `userName` / `email`: Identifying attributes.
- `displayName`: Optional presentation name.
- `status`: `active`, `disabled`, or `locked`.
- `ownership`: `foundation` or `external` (which authority manages this record).
- `tenantMemberships`: Set of `TenantMembership`.
- `directPermissions`: Optional permissions granted directly (outside roles).
- `roles`: Assigned `Role` references (may be tenant-scoped).
- `externalIdentities`: Set of `ExternalIdentity` links.
- `createdAt` / `updatedAt`: Lifecycle timestamps.

### ExternalIdentity

Federation link between an external provider subject and an internal user.

- `provider`: `AuthenticationProvider` id.
- `providerSubject`: The provider's stable subject (`sub`) for the user.
- `userId`: Linked internal `User` id.
- `linkedAt`: When the link was established.
- `lastSeenAt`: Last successful federated sign-in.
- `linkPolicy`: How the link was created (`auto`, `admin`, `invite`).

### Role

A named bundle of permissions assignable to users (and optionally applications).

- `id` / `name`: Stable identifier and display name.
- `description`: Optional explanation.
- `permissions`: Set of `Permission` keys.
- `tenantScope`: `global` or a specific tenant id.
- `system`: Whether the role is built-in/protected from deletion.

### Permission

A discrete capability from the shared catalog. Used by backend enforcement and
Studio presentation.

- `key`: Stable dotted key (e.g. `workflows.read`, `identity.users.manage`).
- `displayName`: Human-readable label.
- `category`: Grouping (e.g. workflows, identity, diagnostics).
- `description`: What the permission allows.
- `implies`: Optional set of permission keys this permission entails.

### Application / Client

An interactive or machine client able to obtain tokens/credentials.

- `id` / `clientId`: Internal id and protocol client id.
- `displayName`: Presentation name.
- `type`: `confidential` or `public`.
- `allowedGrantTypes`: e.g. `authorization_code`, `client_credentials`,
  `refresh_token`.
- `redirectUris` / `postLogoutRedirectUris`: For interactive clients.
- `scopes`: Allowed `Scope` keys.
- `secrets`: Set of `Credential` (hashed client secrets) for confidential clients.
- `ownership`: `foundation` or `external`.
- `tenantScope`: `global` or a tenant id.

### Scope / Audience

Token access boundary describing reachable APIs/resources.

- `key`: Stable scope name (e.g. `elsa.api`, `workflows.read`).
- `displayName` / `description`: Presentation metadata.
- `resources`/`audiences`: APIs that accept tokens carrying this scope.

### Credential / ApiKey

A non-interactive secret bound to an application or user.

- `id`: Stable identifier.
- `subjectType`: `application` or `user`.
- `subjectId`: Bound application or user id.
- `kind`: `api-key` or `client-secret`.
- `hashedSecret`: Stored hash (never plaintext at rest).
- `scopes`: Permitted scopes for tokens/requests using this credential.
- `status`: `active`, `rotating`, `revoked`.
- `expiresAt`: Optional expiry.
- `rotation`: `{ previousId, rotatedAt, graceUntil }` for overlap windows.
- `audit`: Lifecycle events (created, used-first, rotated, revoked).

### TenantMembership

A user's membership and grant overlay within a tenant.

- `tenantId`: Tenant identifier.
- `userId`: Member user id.
- `roles`: Tenant-scoped role assignments.
- `permissions`: Optional tenant-scoped direct permissions.
- `status`: `active` or `suspended`.

## Authentication & Configuration

### AuthenticationProvider

A configured way to authenticate principals.

- `id`: Stable provider id (e.g. `builtin`, `entra`, `keycloak`).
- `kind`: `builtin-credentials`, `openiddict`, `external-oidc`.
- `displayName`: Presentation name for sign-in selection.
- `enabled`: Whether the provider is active.
- `isDefault`: Whether it is the default challenge provider.
- `settings`: Provider-specific configuration (authority, clientId, clientSecret,
  scopes, callback paths, metadata address, claim types). Validated on save.
- `capabilities`: `ProviderCapabilities` flags (see below).
- `tenantScope`: `global` or a tenant id.

### ProviderCapabilities

Capability flags that drive management UX and behavior. A provider advertises what
it supports so Studio enables/disables actions accordingly.

- `supportsLocalUserManagement`: Can users be created/edited in Elsa?
- `supportsLocalRoleManagement`: Can roles be managed in Elsa?
- `supportsApplicationManagement`: Can apps/clients be managed in Elsa?
- `supportsGroupSync`: Does it import groups/claims for mapping?
- `supportsTokenIssuance`: Does it issue tokens (vs only validate)?
- `supportsRefresh`: Does it support refresh tokens?
- `supportsRevocation`: Does it support token revocation?

### ClaimMappingRule

Projects external claims/groups onto Elsa roles/permissions.

- `id`: Stable rule id.
- `provider`: Provider this rule applies to.
- `matchClaimType` / `matchValue`: The incoming claim/group to match.
- `grantRoles`: Roles granted on match.
- `grantPermissions`: Permissions granted on match (optional, for fine rules).
- `order`: Evaluation order.
- `stopOnMatch`: Whether to halt rule evaluation after a match.

### OwnershipMode

The configured authority for identity resources, with derived capability gates.

- `mode`: `foundation-owned` (default), `external-owned`, or `hybrid`.
- `userAuthority`: `foundation` or `external`.
- `roleAuthority`: `foundation` or `external`.
- `applicationAuthority`: `foundation` or `external`.
- `effectiveCapabilities`: Computed from `mode` + active provider capabilities;
  determines whether Studio shows local management as enabled, disabled, or
  read-only.

## Authorization Runtime Concepts

### EffectivePermissionSet

The resolved set of permissions for a principal in a tenant context. Computed from
direct grants, role-derived grants, and mapped external claims. Carried as claims
on the principal and/or evaluated server-side.

- `subjectId`: User or application id.
- `tenantId`: Active tenant.
- `permissions`: Resolved permission keys.
- `source`: Provenance per permission (`direct`, `role:<id>`, `map:<rule>`).
- `evaluatedAt`: When resolved (informs propagation/freshness behavior).

### AuthSession (Studio-facing projection)

The provider-agnostic session state Studio consumes (see contracts).

- `isAuthenticated`: Whether a principal is present.
- `subject`: User id / name / display.
- `tenantId`: Active tenant.
- `permissions`: Effective permission keys for UI gating.
- `provider`: Active provider id and kind.
- `tokenFreshness`: `fresh`, `refreshing`, or `expired`.

### AuthEvent (audit)

An auditable authentication/authorization/identity event.

- `id` / `timestamp`: Identity and time.
- `category`: `authentication`, `authorization`, `identity-change`,
  `credential-lifecycle`.
- `actor`: Subject performing the action (or `anonymous`/`system`).
- `target`: Affected resource (user, role, application, credential, provider).
- `outcome`: `success`, `denied`, `failure`.
- `tenantId`: Tenant context.
- `detail`: Structured, non-sensitive context (never secrets/tokens).

## Relationships (summary)

- `User` *N—N* `Role`; `Role` *N—N* `Permission`.
- `User` *1—N* `ExternalIdentity`; each `ExternalIdentity` references one
  `AuthenticationProvider`.
- `Application` *1—N* `Credential`; `Application` *N—N* `Scope`.
- `AuthenticationProvider` *1—N* `ClaimMappingRule`.
- `OwnershipMode` + active `AuthenticationProvider.capabilities` →
  `effectiveCapabilities` consumed by Studio.
- `User`/`Application` + `Tenant` → `EffectivePermissionSet` → `AuthSession`.

## Reference-stack mapping (non-normative)

- `User`, `Role`, `Permission` (as role claims) → ASP.NET Core Identity entities
  with a custom/EF store.
- `Application`, `Scope`, `Credential`, token issuance → OpenIddict
  application/scope/token stores.
- `AuthenticationProvider` (external-oidc) → ASP.NET Core OIDC/JWT-bearer handler
  configuration.
- `ExternalIdentity`, `ClaimMappingRule`, `OwnershipMode` → foundation-owned
  entities with no direct ASP.NET Core Identity equivalent.

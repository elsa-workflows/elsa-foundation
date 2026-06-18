# Contract: Identity Management API

This contract describes the backend management API surface for users, roles,
permissions, applications, providers, claim mappings, and credentials. It is
provider-agnostic: the same API serves the batteries-included (ASP.NET Core
Identity + OpenIddict) stack and external/hybrid ownership modes, adjusting
behavior through capabilities.

## Conventions

- Base path: `/_elsa/identity` (backend foundation), mirroring the existing
  `/_elsa/...` convention in this repo.
- All endpoints are tenant-scoped via the resolved tenant context.
- All endpoints are gated by permissions from the shared catalog.
- Write endpoints that are unavailable in the active ownership mode MUST return a
  capability-aware response (`409`/`403` with a machine-readable
  `capabilityDisabled` reason), not a generic error.
- Mutations emit `AuthEvent` audit records.

## Capabilities discovery

### `GET /_elsa/identity/capabilities`

Returns the effective capability gates so Studio renders correct affordances.

```json
{
  "ownershipMode": "foundation-owned",
  "userAuthority": "foundation",
  "roleAuthority": "foundation",
  "applicationAuthority": "foundation",
  "capabilities": {
    "supportsLocalUserManagement": true,
    "supportsLocalRoleManagement": true,
    "supportsApplicationManagement": true,
    "supportsGroupSync": false,
    "supportsTokenIssuance": true,
    "supportsRefresh": true,
    "supportsRevocation": true
  },
  "providers": [
    { "id": "builtin", "kind": "openiddict", "isDefault": true, "enabled": true }
  ]
}
```

Permission: `identity.read`.

## Users

- `GET /_elsa/identity/users` — list/search (paged, filterable by status, role,
  provider). Permission: `identity.users.read`.
- `GET /_elsa/identity/users/{id}` — details incl. roles, direct permissions,
  external identities, tenant memberships. Permission: `identity.users.read`.
- `POST /_elsa/identity/users` — create (foundation-owned only). Permission:
  `identity.users.manage`.
- `PUT /_elsa/identity/users/{id}` — update profile/status/roles. Permission:
  `identity.users.manage`.
- `POST /_elsa/identity/users/{id}/lock` / `.../unlock` — status changes.
  Permission: `identity.users.manage`.
- `DELETE /_elsa/identity/users/{id}` — delete/deactivate with lockout safeguard.
  Permission: `identity.users.manage`.
- `GET /_elsa/identity/users/{id}/effective-permissions` — resolved
  `EffectivePermissionSet` for a tenant. Permission: `identity.users.read`.

User payload (response):

```json
{
  "id": "usr_123",
  "userName": "alice",
  "email": "alice@example.com",
  "displayName": "Alice",
  "status": "active",
  "ownership": "foundation",
  "roles": ["workflow-operator"],
  "directPermissions": [],
  "externalIdentities": [
    { "provider": "entra", "providerSubject": "aad|...", "linkedAt": "2026-..." }
  ],
  "tenantMemberships": [{ "tenantId": "default", "roles": ["admin"] }]
}
```

## Roles & permissions

- `GET /_elsa/identity/roles` — list. Permission: `identity.roles.read`.
- `POST /_elsa/identity/roles` — create. Permission: `identity.roles.manage`.
- `PUT /_elsa/identity/roles/{id}` — update name/description/permissions.
  Permission: `identity.roles.manage`.
- `DELETE /_elsa/identity/roles/{id}` — delete (system roles protected).
  Permission: `identity.roles.manage`.
- `GET /_elsa/identity/permissions` — the shared permission catalog (keys,
  categories, descriptions, implications). Permission: `identity.read`.
- `POST /_elsa/identity/roles/{id}/preview` — preview effective permissions a role
  would grant (policy preview). Permission: `identity.roles.read`.

## Applications / clients

- `GET /_elsa/identity/applications` — list. Permission: `identity.apps.read`.
- `POST /_elsa/identity/applications` — register (type, grants, redirect URIs,
  scopes). Permission: `identity.apps.manage`.
- `PUT /_elsa/identity/applications/{id}` — update. Permission:
  `identity.apps.manage`.
- `DELETE /_elsa/identity/applications/{id}` — remove. Permission:
  `identity.apps.manage`.
- `GET /_elsa/identity/scopes` — available scopes/audiences. Permission:
  `identity.read`.

## Credentials & rotation

- `GET /_elsa/identity/applications/{id}/credentials` — list (metadata only, never
  secrets). Permission: `identity.apps.read`.
- `POST /_elsa/identity/applications/{id}/credentials` — issue a new API
  key/client secret; plaintext returned **once** at creation. Permission:
  `identity.credentials.manage`.
- `POST /_elsa/identity/credentials/{credId}/rotate` — rotate with optional grace
  window; old credential invalidated per policy. Permission:
  `identity.credentials.manage`.
- `POST /_elsa/identity/credentials/{credId}/revoke` — immediate revoke.
  Permission: `identity.credentials.manage`.

Rotation response:

```json
{
  "id": "cred_new",
  "kind": "api-key",
  "plaintext": "shown-once-...",
  "previousId": "cred_old",
  "graceUntil": "2026-06-25T00:00:00Z",
  "scopes": ["elsa.api"]
}
```

## Providers

- `GET /_elsa/identity/providers` — list with capabilities, enabled/default flags.
  Permission: `identity.providers.read`.
- `POST /_elsa/identity/providers` — add/configure a provider. Settings validated
  via `ValidateSettings`; validation errors returned field-by-field. Permission:
  `identity.providers.manage`.
- `PUT /_elsa/identity/providers/{id}` — update settings/enabled/default.
  Permission: `identity.providers.manage`.
- `POST /_elsa/identity/providers/{id}/test` — connectivity/metadata test (e.g.
  discovery document reachable). Permission: `identity.providers.manage`.
- `DELETE /_elsa/identity/providers/{id}` — remove. Permission:
  `identity.providers.manage`.

Provider settings are secret-aware: secret fields are write-only and never
returned; responses indicate presence (`hasClientSecret: true`).

## Claim / role mappings

- `GET /_elsa/identity/providers/{id}/mappings` — list rules. Permission:
  `identity.providers.read`.
- `POST /_elsa/identity/providers/{id}/mappings` — add a mapping rule (match
  claim/group → roles/permissions, order, stopOnMatch). Permission:
  `identity.providers.manage`.
- `PUT /_elsa/identity/mappings/{ruleId}` / `DELETE .../{ruleId}` — edit/remove.
  Permission: `identity.providers.manage`.

## Audit

- `GET /_elsa/identity/audit` — paged, filterable auth/identity events (no
  secrets/tokens in detail). Permission: `identity.audit.read`.

## Error & capability semantics

```json
{
  "error": "capabilityDisabled",
  "capability": "supportsLocalUserManagement",
  "ownershipMode": "external-owned",
  "message": "Users are managed by the external identity provider."
}
```

Studio uses `capabilityDisabled` plus the capabilities endpoint to present
management surfaces as enabled, disabled, or read-only rather than failing
opaquely.

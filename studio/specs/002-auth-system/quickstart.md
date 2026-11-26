# Quickstart: Validating the Auth System

This quickstart describes how to validate each supported deployment profile. It is
written against the foundation contracts and reference stack; concrete commands
will be filled in as implementation lands.

## Prerequisites

- `elsa-foundation` backend host with the Identity abstractions package referenced.
- `elsa-foundation-studio` Studio shell with the auth SDK wired into the app root.
- A test tenant (`default`) and at least one seed admin (foundation-owned mode).

## Profile A — External IdP + Foundation IAM (default)

1. Configure the external OIDC provider (authority, client id/secret, scopes,
   redirect URI) via `POST /_elsa/identity/providers`.
2. Run `POST /_elsa/identity/providers/{id}/test` and confirm the discovery
   document is reachable.
3. Sign in through Studio; confirm redirect → callback → authenticated
   `AuthSession` with normalized `roles`/`permissions`.
4. Confirm a new external user is linked to an internal `User` with an
   `ExternalIdentity` row.
5. Confirm capabilities show `ownershipMode: foundation-owned` and local user
   management enabled.

## Profile B — OpenIddict + Foundation IAM (self-hosted)

1. Enable the OpenIddict reference provider; ensure a strong signing key is
   configured (startup MUST reject dev/default keys outside development/demo).
2. Register an interactive client and an API client via
   `POST /_elsa/identity/applications`.
3. Sign in with a local user; confirm token issuance, refresh, and revocation
   behave per `ProviderCapabilities`.
4. Issue an API key, call a backend endpoint with it, then rotate and confirm the
   grace window behavior.

## Profile C — External-IdP-owned IAM

1. Set ownership mode to `external-owned`.
2. Confirm `GET /_elsa/identity/capabilities` reports user/role/app authority as
   external and local management disabled/read-only.
3. Define a group→role mapping; sign in a federated user in a mapped group.
4. Confirm mapped roles/permissions apply and that attempting a local user create
   returns `capabilityDisabled` (not a generic error).
5. Confirm Studio renders user management as read-only with an explanatory state.

## Profile D — Legacy compatibility

1. Point the legacy adapter at existing Elsa Identity stores.
2. Sign in with an existing legacy user; confirm a normalized session.
3. Run the import tool (optional) and confirm users/roles/api-keys + external
   identity links land in the reference stores.
4. Confirm no deprecated `Login` UI is used; local-credential login flows only
   through the adapter.

## Authorization parity check (all profiles)

1. Assign a user a role lacking `workflows.write`.
2. Force-enable the corresponding Studio control (dev override) and attempt the
   action; confirm the **backend denies** it.
3. Confirm Studio, by default, hides or disables the control via `usePermissions`.
4. Verify the permission-propagation timing matches the documented behavior for
   the active provider (immediate vs next refresh).

## Audit & security checks

- Confirm authentication, authorization denials, and credential changes emit
  `AuthEvent`s with no secrets/tokens in detail.
- Confirm secrets/API keys are stored hashed and never returned after creation.
- Confirm production rejects missing/weak signing keys and non-HTTPS metadata.

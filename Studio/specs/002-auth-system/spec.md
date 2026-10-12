# Feature Specification: Elsa Foundation Auth System

**Feature Branch**: `002-auth-system`

**Created**: 2026-06-18

**Status**: Draft

**Input**: User description: "Establish a solid, extensible authentication and authorization story for Elsa Foundation and Elsa Foundation Studio, grounded in ASP.NET Core auth primitives and delivered as modules. Evaluate whether OpenIddict should be the default instead of the custom Elsa Identity module, and design an ideal solution for managing users, roles, applications, and OIDC providers."

## Overview

Elsa Foundation needs one coherent authentication and authorization story that
works across deployment shapes: enterprise installs that already own an identity
provider, self-hosted installs that want a turnkey login experience, and
developers who want to run locally in seconds. The system MUST be modular so a
developer chooses how identity is owned and how users authenticate without
forking the architecture, and Elsa Foundation Studio MUST present a single,
provider-agnostic experience for signing in and for managing identity resources.

This specification describes the *behavior and value* of that system. Technology
choices (ASP.NET Core Identity, OpenIddict, ASP.NET Core authentication handlers)
are recorded in [plan.md](./plan.md) and [adr/0001-auth-architecture.md](./adr/0001-auth-architecture.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Provider-Agnostic Sign-In (Priority: P1)

As an operator deploying Elsa Foundation, I want to choose how users sign in —
an external identity provider, a built-in login, or both — through configuration
and modules, so that Studio and the backend authenticate users consistently
regardless of the chosen provider.

**Why this priority**: Authentication is the entry point. Nothing else in the
system is reachable or testable until a principal can sign in and the backend can
validate that principal. This is the MVP.

**Independent Test**: Configure the host with one provider (e.g. a built-in
login), sign in through Studio, and confirm authenticated API and SignalR calls
succeed and that sign-out clears the session — without any provider-specific code
in feature modules.

**Acceptance Scenarios**:

1. **Given** a host configured with a single authentication provider, **When** a
   user opens Studio while unauthenticated, **Then** they are guided through that
   provider's sign-in and returned to Studio as an authenticated principal.
2. **Given** an authenticated Studio session, **When** Studio calls a protected
   backend endpoint or opens a SignalR stream, **Then** the request carries valid
   credentials and is authorized without per-module token handling.
3. **Given** an authenticated session whose access token is near expiry, **When**
   the user continues working, **Then** the session is refreshed transparently or,
   if refresh is impossible, the user is cleanly returned to sign-in.
4. **Given** a host configured to switch from one provider to another, **When**
   only configuration/modules change, **Then** feature modules and Studio screens
   require no code changes to keep working.

---

### User Story 2 - Managing Users, Roles, and Permissions (Priority: P2)

As an administrator, I want to manage users, roles, and permissions inside Elsa
when Elsa owns identity, so that I can grant least-privilege access to workflows,
modules, and administrative actions without an external system.

**Why this priority**: The "batteries-included" promise depends on first-class
identity management. It proves the IAM domain model and the authorization
contract that every protected feature relies on.

**Independent Test**: With Elsa configured as the identity owner, create a role
with a scoped permission set, assign it to a user, sign in as that user, and
confirm they can perform exactly the permitted actions and are denied others.

**Acceptance Scenarios**:

1. **Given** an admin with user-management permission, **When** they create a
   user and assign roles, **Then** the user can sign in and receives exactly the
   permissions implied by their direct and role-derived grants.
2. **Given** a role whose permissions change, **When** the change is saved,
   **Then** affected users' effective permissions reflect the change on their next
   evaluated request (and the system documents whether existing sessions update
   immediately or on token refresh).
3. **Given** a non-privileged user, **When** they attempt a management action,
   **Then** they are denied through the shared authorization pipeline with a clear,
   non-leaking error.
4. **Given** an admin removing the last privileged user or role, **When** they
   attempt it, **Then** the system prevents lockout or warns according to a
   documented safeguard.

---

### User Story 3 - Managing Applications, Clients, and API Keys (Priority: P3)

As an integrator, I want to register applications/clients and issue
non-interactive credentials (API keys / client credentials), so that services,
CI jobs, and scripts can call Elsa securely with scoped, rotatable credentials.

**Why this priority**: Machine-to-machine access is required for real automation
and is a distinct lifecycle from interactive users. It exercises scopes,
audiences, and credential rotation.

**Independent Test**: Register a confidential client with a limited scope, obtain
a credential, call a protected endpoint within scope (success) and outside scope
(denied), then rotate the credential and confirm the old one stops working.

**Acceptance Scenarios**:

1. **Given** an admin, **When** they register an application with allowed grant
   types and scopes, **Then** the application can obtain tokens/credentials only
   for the configured grants and scopes.
2. **Given** an issued API key or client secret, **When** the admin rotates it,
   **Then** new credentials are accepted, the previous credential is invalidated
   per policy, and the action is auditable.
3. **Given** a client token presented to the backend, **When** the requested
   resource requires a scope the token lacks, **Then** the request is denied with
   a scope/audience-appropriate response.

---

### User Story 4 - Configuring and Federating OIDC Providers (Priority: P4)

As an operator, I want to configure one or more external OIDC providers and map
their claims/groups onto Elsa roles and permissions, so that existing enterprise
identities work in Elsa without recreating users.

**Why this priority**: Enterprise adoption depends on federation. It proves the
external-IdP ownership mode and the claims-to-permission mapping contract.

**Independent Test**: Configure an external OIDC provider, sign in as a federated
user, and confirm an external group/claim is mapped to an Elsa role granting the
expected permissions, with the external identity linked to an internal principal.

**Acceptance Scenarios**:

1. **Given** a configured external OIDC provider, **When** a federated user signs
   in for the first time, **Then** the system establishes a linked internal
   principal according to the active ownership mode.
2. **Given** mapping rules from provider groups/claims to Elsa roles, **When** a
   federated user signs in, **Then** their effective permissions reflect the
   mapped roles.
3. **Given** multiple enabled providers, **When** a user signs in, **Then** they
   can select or are routed to the correct provider, and the resulting principal
   is normalized to the same internal claims/permission shape.
4. **Given** an ownership mode where the external IdP owns users, **When** an
   admin opens user management in Studio, **Then** local user creation is clearly
   disabled or read-only, with provider-owned data presented appropriately.

---

### User Story 5 - Consistent Authorization Across Backend and Studio (Priority: P5)

As a module author, I want one authorization model shared by the backend and
Studio, so that a permission protects an API endpoint and also drives whether the
corresponding UI affordance is shown or enabled.

**Why this priority**: Cohesion between backend enforcement and UI presentation
prevents drift and security-by-obscurity mistakes. It can land after the core
authentication and IAM flows are proven.

**Independent Test**: Define one permission, protect a backend endpoint with it,
and gate a Studio action on it; confirm a user lacking the permission is denied by
the API and does not see/enable the action in Studio.

**Acceptance Scenarios**:

1. **Given** a permission protecting an endpoint, **When** Studio renders a screen
   using that endpoint, **Then** Studio reflects the user's permission (hidden,
   disabled, or read-only) using shared, provider-agnostic auth state.
2. **Given** the backend as the source of truth for enforcement, **When** Studio
   shows an affordance the user lacks permission for due to stale state, **Then**
   the backend still denies the action and Studio handles the denial gracefully.

### Edge Cases

- A provider returns no refresh token; session handling MUST degrade to a clean
  re-authentication rather than a broken session.
- A federated user's external subject changes or is reused; linking rules MUST
  avoid silently merging or hijacking accounts.
- Two providers assert the same email; the system MUST follow a documented
  account-linking policy rather than auto-merging.
- Ownership mode disables local user/app management; Studio MUST present this as
  an intentional, explained state, not an error.
- Clock skew, expired signing keys, or rotated provider keys MUST be handled with
  metadata refresh and clear failure messaging.
- A misconfigured provider (bad authority, wrong redirect URI, missing scope)
  MUST surface actionable diagnostics, not a blank failure.
- Multi-tenant installs MUST keep users, roles, applications, and provider
  configuration isolated per tenant and resolve the correct tenant for each
  request.
- Development/demo defaults (e.g. weak keys, seeded admins) MUST be rejected or
  gated outside explicit development/demo environments.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST separate the Identity & Access Management domain
  (users, roles, permissions, applications, credentials, tenant membership) from
  the authentication protocol layer (how principals sign in and how tokens are
  issued/validated), so provider choices do not dictate the domain model.
- **FR-002**: The system MUST be modular: authentication providers, the identity
  store, the authorization policy set, and management surfaces MUST be selectable
  and replaceable through modules and configuration without changing feature
  module code.
- **FR-003**: The system MUST support at least three ownership modes —
  foundation-owned IAM (default), external-IdP-owned IAM, and hybrid — as a
  configurable capability rather than separate architectures.
- **FR-004**: The system MUST provide a batteries-included profile that lets an
  operator manage users, roles, permissions, and applications inside Elsa and sign
  in without any external identity provider.
- **FR-005**: The system MUST support integrating one or more external OIDC/OAuth2
  providers and MUST normalize the resulting principal to a single internal
  claims/permission shape.
- **FR-006**: The system MUST map external provider claims/groups to Elsa roles
  and permissions through documented, configurable mapping rules.
- **FR-007**: The system MUST link external identities to internal principals via
  a stable `(provider, providerSubject) → user` association, following a
  documented account-linking policy.
- **FR-008**: Authorization MUST be policy- and permission-based using a shared
  permission catalog, so the same permission can protect a backend endpoint and
  drive a Studio affordance.
- **FR-009**: The system MUST support non-interactive credentials (API keys and/or
  client credentials) bound to applications or users, with scopes, audiences, and
  rotation, and MUST record credential lifecycle events for audit.
- **FR-010**: The system MUST manage token lifecycle (issue, validate, refresh,
  and where applicable revoke) and MUST handle near-expiry refresh transparently
  or fail closed to re-authentication.
- **FR-011**: Studio MUST consume a single provider-agnostic auth contract for
  sign-in, sign-out, session/auth state, claims, and authenticated transport
  (HTTP and SignalR), with provider-specific behavior isolated in adapters.
- **FR-012**: Studio MUST surface provider/ownership capabilities so management
  screens correctly enable, disable, or present-as-read-only the user, role,
  application, and provider management actions for the active mode.
- **FR-013**: The system MUST provide management APIs for users, roles,
  permissions, applications/clients, providers, claim/role mappings, and
  credentials, gated by the shared authorization model.
- **FR-014**: The system MUST be multi-tenant aware, isolating identity resources
  and provider configuration per tenant and resolving the correct tenant per
  request.
- **FR-015**: The system MUST enforce secure-by-default behavior: reject known
  weak/dev signing keys and seeded credentials outside development/demo, require
  transport security for production metadata, and avoid exposing tokens to
  untrusted contexts.
- **FR-016**: The system MUST provide a documented migration path from the
  existing Elsa Identity module and the deprecated `Elsa.Studio.Login`-era setup,
  including a compatibility mode and a staged deprecation sequence.
- **FR-017**: Authentication and authorization decisions, plus identity-resource
  changes, MUST emit auditable events.
- **FR-018**: The backend MUST remain the source of truth for authorization
  enforcement; Studio permission-awareness is a presentation optimization and MUST
  NOT be the sole gate.

### Key Entities *(include if feature involves data)*

- **User**: A principal that can authenticate, with profile data, status
  (active/locked), and tenant membership. Canonical internally when Elsa owns IAM.
- **ExternalIdentity**: A link between an external provider subject and an internal
  user (`provider`, `providerSubject`, `userId`), enabling federation.
- **Role**: A named bundle of permissions assignable to users (and possibly
  applications), optionally tenant-scoped.
- **Permission**: A discrete capability (e.g. `workflows.read`) from a shared
  catalog, used by both backend enforcement and Studio presentation.
- **Application/Client**: An interactive or machine client with allowed grant
  types, redirect URIs (interactive), scopes, and associated secrets/keys.
- **Scope/Audience**: Token access boundaries describing which APIs/resources a
  token may reach.
- **Credential/ApiKey**: A non-interactive secret bound to an application or user,
  with scopes, rotation metadata, and lifecycle/audit history.
- **AuthenticationProvider**: A configured way to authenticate (built-in,
  external OIDC, etc.) with capability flags and provider-specific settings.
- **ClaimMappingRule**: A rule that projects external claims/groups onto Elsa
  roles/permissions.
- **TenantMembership**: A user's membership and role/grant overlay within a
  tenant.
- **OwnershipMode**: The configured authority for identity resources
  (foundation-owned, external-IdP-owned, hybrid) with associated capability flags.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can stand up a working batteries-included login with
  managed users and roles and no external identity provider, following the
  quickstart.
- **SC-002**: An operator can switch the authentication provider (e.g. built-in to
  external OIDC) by changing only configuration/modules, with zero changes to
  feature module or Studio screen code.
- **SC-003**: A single defined permission simultaneously protects a backend
  endpoint and gates a Studio affordance, demonstrated end to end.
- **SC-004**: A federated external-OIDC user signs in and receives Elsa
  permissions derived from mapped external groups/claims, with the external
  identity linked to an internal principal.
- **SC-005**: An application credential can be issued, used within scope, denied
  outside scope, rotated, and the old credential invalidated, all auditable.
- **SC-006**: Each ownership mode presents correct Studio management capabilities
  (enabled, disabled, or read-only) without code branches in feature modules.
- **SC-007**: No deployment outside an explicit development/demo environment can
  start with a known weak signing key or default seeded credential.
- **SC-008**: An existing Elsa Identity / `Elsa.Studio.Login` deployment can move
  to the new system through the documented compatibility and migration path
  without losing user/role/application data.

## Assumptions

- `elsa-foundation` (the backend foundation) and `elsa-foundation-studio` (this
  repo's shell) are the delivery surfaces; some backend contracts are designed
  here but implemented in `elsa-foundation`.
- ASP.NET Core authentication/authorization primitives are the substrate; the
  batteries-included reference stack is ASP.NET Core Identity for user/role
  management plus OpenIddict for first-party token issuance, kept behind
  provider-agnostic contracts (see plan and ADR).
- External OIDC integration is the default *posture* for enterprise installs;
  OpenIddict is shipped as an optional first-party provider, not a hard default.
- The existing modular host model (CShells feature toggles) is the composition
  mechanism for enabling/disabling auth modules.
- The system is adopted incrementally; legacy Elsa Identity / Login setups must
  keep working during a compatibility window.
- Studio remains the primary administrative UI for identity resources where the
  active ownership mode permits local management.

# Feature Specification: External authentication and OIDC connections

**Feature Branch**: `agent/codex/3579-external-auth`  
**Created**: 2026-11-04  
**Status**: Implemented
**Input**: [#3579](https://github.com/elsa-workflows/elsa-foundation/issues/3579), authorized for end-to-end delivery by Sipke on 4 November 2026. Program: [Elsa 3 parity #2771](https://github.com/elsa-workflows/elsa-foundation/issues/2771). Decisions: [ADR 0085](../../docs/adr/0085-elsa-3-parity-identity-administration-and-scope.md), with the explicit override amendment authorized in #3579. Necessary prerequisite: [#2848](https://github.com/elsa-workflows/elsa-foundation/issues/2848).

## User Scenarios & Testing

### User Story 1 — Choose a provider and sign in as the intended Elsa user (Priority: P1)

A user opens the backend login page, chooses local credentials or one of several enabled external providers, and receives their normal Elsa session and Elsa-authorized permissions. Preferred methods are emphasized while the chooser remains visible.

**Why this priority**: Connection administration is useful only when sign-in establishes the correct identity and authorization boundary.

**Independent Test**: Compose local login and two controlled OIDC providers, complete each real sign-in, exchange the resulting session for an Elsa token, access a protected resource, and sign out.

**Acceptance Scenarios**:

1. **Given** two enabled external connections and available local credentials, **when** the user visits login, **then** all three methods are offered without automatic redirection past the chooser.
2. **Given** a linked external identity, **when** its provider authenticates it, **then** Elsa resolves the active user using tenant, logical provider key, issuer and subject, applies Elsa roles, and establishes the normal session/token pipeline.
3. **Given** an unknown external identity and the default policy, **when** sign-in completes upstream, **then** Elsa refuses admission without creating a user, link or session.
4. **Given** explicit connection-level provisioning and authorized default roles, **when** an unknown identity signs in, **then** exactly one user and link are created, without a generated local password.
5. **Given** configured existing-user matching, **when** its required evidence uniquely identifies an eligible user, **then** the link is established under that explicit policy; absent, ambiguous or unverified evidence refuses admission.
6. **Given** equal upstream subjects in different providers, issuers or tenants, **when** one user signs out or is revoked, **then** the other users' sessions and refresh credentials remain usable.

### User Story 2 — Configure, test, preview and enable a connection (Priority: P1)

An administrator configures a disabled draft, supplies secret references, tests connectivity, previews actual sign-in and its prospective admission and permissions, then enables the connection. Each step explains its outcome and the next useful action.

**Why this priority**: Administrators need to qualify a provider before making it a login path.

**Independent Test**: Use Studio and its API to create a disabled connection, fix an actionable test failure, run a real interactive preview, inspect its result, and enable the connection without restarting a host.

**Acceptance Scenarios**:

1. **Given** database management is enabled and the administrator has permission, **when** they create a connection, **then** it starts disabled and displays its logical key, source, callback URL, validity and observed test result separately.
2. **Given** a disabled draft and valid upstream credentials, **when** the administrator previews sign-in, **then** the result describes identity, admission, roles and permissions without creating users, links or normal sessions or replacing the administrator's session.
3. **Given** a malformed setting, unresolved secret, failed discovery or failed upstream authentication, **when** the administrator tests or previews, **then** diagnostics identify a safe actionable category without exposing secrets, authorization codes, tokens or unfiltered claims.
4. **Given** a structurally valid connection with resolvable required secrets, **when** it is enabled, **then** all nodes offer it on subsequent login discovery without restart; a previous successful test does not override later invalid configuration.

### User Story 3 — Respect deployment ownership through the override lifecycle (Priority: P1)

A deployment owner keeps providers in configuration. An authorized administrator can deliberately create a full Studio override only where deployment policy permits it, with an explanation of the resulting login behavior.

**Why this priority**: Source changes must be predictable and preserve established links.

**Independent Test**: Exercise a baseline and same-key database record through explicit promotion, disable, archive, restore and removal, signing in with an existing link across source transitions.

**Acceptance Scenarios**:

1. **Given** a deployment baseline and an ordinary same-key database record, **when** effective connections are read, **then** the baseline stays effective and Studio explains the collision; no implicit override occurs.
2. **Given** deployment policy allows an explicit full override, **when** the administrator confirms its promotion, **then** the entire selected database document becomes effective without field merging.
3. **Given** an effective override, **when** it is disabled, **then** it continues to shadow the baseline and the logical connection is unavailable for normal login.
4. **Given** an effective override, **when** it is archived or removed, **then** the baseline becomes effective; restoring the override restores its full document and lifecycle state.
5. **Given** an existing external identity link, **when** connection source changes, **then** the stable logical key retains that link while a different issuer or subject does not inherit it.
6. **Given** a stale edit or two concurrent mutations, **when** changes race, **then** stale changes are rejected and the resulting effective registry obeys the same ownership and final-login invariants.

### User Story 4 — Administer identity links, sessions and recovery (Priority: P1)

A security administrator inspects external links and sessions, explicitly links or unlinks an identity, revokes access, and receives redacted security notifications. Administration cannot silently remove the final usable login path.

**Why this priority**: SSO must remain recoverable and manageable after initial setup.

**Independent Test**: Manage links and sessions through Studio/API, revoke an external session and attempt access/refresh, then try mutations that would remove the final usable login method, including concurrent changes on separate nodes.

**Acceptance Scenarios**:

1. **Given** a security administrator, **when** they inspect a user, **then** links identify the provider, issuer and subject within their tenant boundary and available sessions show safe metadata and revocation state.
2. **Given** a link already belongs to another user, **when** an administrator tries to attach it, **then** the operation refuses with a conflict and preserves both users' identities.
3. **Given** a live external session, **when** it is revoked or its user is disabled/deleted, **then** its cookie, access and refresh credentials cease granting access under the documented session boundary.
4. **Given** only one usable sign-in path remains, **when** a connection mutation would remove it, including replacing it with a disabled override or revealing an unusable baseline, **then** the mutation refuses and explains recovery options.
5. **Given** a read-only operator or missing optional modules, **when** Studio displays security administration, **then** available actions match actual capabilities and permissions and unavailable actions provide appropriate guidance.

### User Story 5 — Continue safely across restart and multiple nodes (Priority: P1)

An operator runs a durable deployment whose connection changes, identity state, preview results and authentication transactions survive process loss and remain safe when requests move between nodes.

**Why this priority**: Provider sign-in crosses multiple requests and cannot depend on one process's memory.

**Independent Test**: Start login/preview on node A, stop it or route callback to node B with the supported shared storage/key configuration, finish once, reject replay, and verify provider lifecycle changes on both nodes.

**Acceptance Scenarios**:

1. **Given** a durable supported composition, **when** a host restarts, **then** provider configuration, links and revocation remain effective and an unexpired authentication flow can complete.
2. **Given** two nodes, **when** a callback or preview result is consumed concurrently, **then** only one completion succeeds and no duplicate identity or session is created.
3. **Given** connection material or secret generation changes after login begins, **when** its old callback completes, **then** it is refused safely and the user can restart sign-in against current configuration.

### Edge Cases

- A disabled override is effective but not a usable login method; an archived override reveals the baseline.
- Deployment-only composition neither reads nor writes optional database-managed providers.
- An installed protocol adapter is missing, a secret is unavailable, a provider times out, or cancellation occurs.
- A return URL is external, a callback state is missing/expired/replayed, or the callback arrives on a different node.
- Issuer changes under the same key; equal subjects with different issuers; source transitions with the same issuer.
- Concurrent first sign-ins, conflicting links, ambiguous existing-user matches and a disabled/deleted linked user.
- Host-wide connections do not grant cross-tenant identity or permission union; tenant-targeted provider administration remains deferred.
- Upstream claims contain Elsa-internal claims, conflicting identifiers, unknown roles or direct permission strings.
- A read-only/deployment-owned record, forbidden override, forbidden role or stale revision is submitted directly to the API.
- An upstream session already exists when preview starts; preview still cannot establish a normal Elsa session.
- Light/dark themes, keyboard navigation, narrow layouts, loading/error/empty states and retained Studio tabs.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST support several host-wide identity-provider connections, initially OIDC, alongside existing local and external bearer authentication modes.
- **FR-002**: Provider interaction and login presentation MUST remain backend-owned. Studio MUST receive only Elsa session/token outcomes and safe administration results.
- **FR-003**: Deployment configuration MUST be the read-only baseline. Database management MUST be opt-in and explicitly report its availability.
- **FR-004**: A same-key database row MUST NOT replace a baseline without an explicit full override authorized by deployment policy. Source fields MUST NOT be merged.
- **FR-005**: A disabled effective override MUST continue to shadow; archive/removal MUST reveal the baseline; restore MUST reinstate the override. Mutation confirmation MUST show the resulting effective state.
- **FR-006**: Immutable logical connection keys MUST be distinct from record IDs and persist across lifecycle/source transitions.
- **FR-007**: Studio and API MUST support list/detail, disabled creation, editing, explicit override promotion, enable/disable, archive/restore and removal where policy permits, with conflict detection for stale changes.
- **FR-008**: Connection metadata MUST distinguish configured enablement, structural validity, secret resolution and the latest observed on-demand test outcome.
- **FR-009**: Required client credentials MUST use Elsa Secrets references, never inline connection secrets or readable secret values. Connection APIs, logs and notifications MUST remain redacted.
- **FR-010**: Administrators MUST receive clear required-field guidance and copyable derived callback URLs. Installed adapter metadata MAY supply protocol-specific guidance without creating new protocols.
- **FR-011**: Non-interactive tests MUST report connectivity/configuration findings with safe error categories and MUST NOT provision identities or sessions.
- **FR-012**: Interactive previews MUST use actual provider authentication and explain prospective admission, identity links, roles and Elsa permissions without durable user/link mutation or normal session/token issuance.
- **FR-013**: Preview initiation/results MUST be bound to the authorized administrator, expire, resist replay and be isolated from normal login and existing administrator sessions.
- **FR-014**: The backend chooser MUST show applicable local credentials and enabled valid external providers; a preferred method MUST remain a visible choice without automatic bypass.
- **FR-015**: Upstream authentication MUST validate the issuer, audience, signature, nonce and correlation and use authorization-code flow with PKCE. Return locations and callback URLs MUST obey host policy.
- **FR-016**: Stable links MUST distinguish tenant, logical provider key, issuer and upstream subject. Conflicting ownership MUST be refused atomically.
- **FR-017**: Unlinked identities MUST be refused by default. Per-connection JIT provisioning/default roles and existing-user matching MUST require explicit configuration.
- **FR-018**: Matching MUST never silently infer ownership from email or user name. A configured verified-email matcher MUST require verified evidence and an unambiguous eligible user.
- **FR-019**: Provisioning MUST honor active ownership capabilities and authorization boundaries and MUST NOT create a local password for an external-only user.
- **FR-020**: Integrator provisioning policies MAY assign saved existing Elsa roles when creating a user. Later managed sign-ins MUST use saved Elsa memberships and preserve manual assignments. External group-to-role mappings, session-only mapped grants and provider-managed recurring memberships are excluded for now. External role/group names MUST NOT create role definitions or grant authority by name; upstream permission claims MUST NOT pass through implicitly. Elsa remains the permission authority.
- **FR-021**: Connection administrators MUST NOT grant roles or permissions beyond their authorized/deployment-controlled boundary, including through JIT, matching, custom provisioning policies or overrides.
- **FR-022**: Successful admission MUST establish the existing Elsa session and token pipeline using a canonical, unambiguous identity. Issuance, refresh, sign-out, disable/delete and administrative revocation MUST preserve tenant/provider/issuer isolation as required by #2848.
- **FR-023**: Studio/API MUST support authorized identity-link inspection, explicit link creation/removal and conflict reporting without exposing another tenant's identities.
- **FR-024**: Studio/API MUST support safe session inspection and revocation. Revocation failures MUST remain failures and allow retry without falsely reporting success.
- **FR-025**: Security-relevant provider, admission, linking and session events MUST emit redacted notifications through existing infrastructure, without requiring a new audit database.
- **FR-026**: Every effective-registry mutation MUST guard the final usable login path, including lifecycle/source transitions and concurrent node mutations. A configured but invalid/unusable provider MUST NOT count as recovery.
- **FR-027**: Effective database changes MUST be visible to subsequent discovery/authentication requests across supported nodes without restart, with material-revision checks for in-flight flows.
- **FR-028**: Authentication state and one-time preview/result consumption MUST be durable and atomic across supported nodes; expiry and replay protections MUST hold across restart.
- **FR-029**: Supported deployment modes MUST document shared provider/IAM/token state, key requirements and actual verification. In-memory or node-local compositions MUST NOT be represented as durable multi-node support.
- **FR-030**: API permissions and capability enforcement MUST be authoritative; Studio MUST reflect deployment ownership, active ownership mode, read-only access and unavailable optional modules.
- **FR-031**: Studio MUST follow existing SDK/module/navigation and theme/responsive/accessibility conventions, including clear loading, empty, error, conflict and recovery states.
- **FR-032**: Delivery MUST include backend, API, Studio, operator/extension documentation and reproducible qualification evidence together; no stage alone closes #3579.

### Key Entities

- **Identity-provider connection**: immutable logical key, separate source record ID, display/login presentation, installed protocol type, settings, secret references, admission and role policy, enablement/archive state, explicit override intent and revision.
- **Effective connection**: the selected full document plus provenance, shadow relationships, validity and derived callback metadata.
- **External identity link**: tenant, logical key, issuer, subject and Elsa user, with safe administration metadata.
- **Authentication transaction**: bounded purpose, selected connection/material revision, tenant, safe return location, correlation/nonce/PKCE state, expiry and one-time consumption.
- **Preview result**: administrator-bound, expiring/redacted prospective identity/admission/permissions and diagnostics, separate from a session.
- **Elsa session**: the normal identity/session pipeline with inspectable safe metadata and revocation state; upstream credentials are excluded from administration projections.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A user can choose and complete local login or either of at least two actual OIDC providers, obtaining the intended Elsa authorization and normal session.
- **SC-002**: An administrator completes configure → test → preview → enable, edits and retires/restores connections without restarting any supported node; every source/override lifecycle acceptance scenario passes.
- **SC-003**: Default rejection, explicit JIT, explicit matching, integrator provisioning roles, link conflict handling and disabled-user rejection produce exactly the expected user/link/session changes and permissions.
- **SC-004**: Preview creates zero users, links, normal sessions or normal tokens and leaves the administrator's existing session intact; replay and cross-administrator retrieval are refused.
- **SC-005**: Equal-subject isolation and revoke/sign-out/disable/delete tests prove no cross-user/provider/issuer/tenant revocation or authorization leakage.
- **SC-006**: Final-login protection refuses every covered last-path mutation, including a concurrent two-node case, while allowing safe baseline-revealing changes.
- **SC-007**: Actual restart and two-node tests complete unexpired flows once, reject replay, observe provider changes and preserve intended links and revocation in every deployment mode claimed.
- **SC-008**: Representative Studio journeys pass for deployment-only/read-only, permitted editing, denied permissions, ownership modes, optional-module absence, light/dark themes and narrow layouts.
- **SC-009**: The final reviewed head passes required repository checks, merges through the applicable review rules, and resulting main repeats the affected acceptance journeys and consumer/artifact checks. Evidence distinguishes pass, fail, skip, unavailable, cancellation and incomplete work.

## Assumptions

- The existing parity program remains active; the 4 November authorization advances only this external authentication scope and its necessary identity-isolation prerequisite.
- The owner-selected explicit override behavior is settled. ADR 0085's other accepted decisions remain applicable.
- Per-tenant provider administration, one-shot Elsa 3 identity/connection import, SAML/LDAP, a new identity authority and new audit storage are follow-up scope.
- Existing authentication modes remain supported. Elsa 3 sources guide behavior; their Blazor hosting, broker/client-token plumbing and duplicate identity infrastructure are not requirements for Elsa 4.
- A controlled local OIDC provider is acceptable for runtime qualification. All hosts, accounts, keys and stores used for qualification are owned temporary resources; production configuration and credentials are excluded.
- Test/preview results describe the observed revision and time, not a guarantee that the upstream provider will remain available.

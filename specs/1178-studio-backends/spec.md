# Feature Specification: Studio backend connections

**Feature Branch**: `agent/codex/3531-backend-connections`
**Created**: 2026-10-31
**Status**: Approved
**Input**: Owner-approved backend connection design and instruction to deliver end to end; #3531. Program-goal state: none/free-flow.

## User Scenarios & Testing

### User Story 1 - Manage connection profiles (Priority: P1)

Developers configure named connections. Users inspect them and, when allowed, manage personal profiles. The list, details and create/edit forms are separate pages. Details has Connection and Capabilities tabs.

**Independent Test**: Configure two backends, create and edit a personal profile, reload, and verify ownership and identity isolation.

**Acceptance Scenarios**:
1. Configured profiles are source-owned; a user can inspect but cannot edit or remove them.
2. Creating or editing a personal profile saves only profile metadata and leaves the active connection unchanged. Cancel discards form edits.
3. Profile persistence is bounded and scoped to the Studio host, user and tenant. No token, password, server management address or management key enters a personal profile.
4. Existing single-backend configuration continues to work without migration.

### User Story 2 - Select a backend safely (Priority: P1)

The shell shows the active backend and offers a quick switcher in both navigation placements. Each browser tab owns its active selection.

**Independent Test**: Switch between two real backends with different datasets and credentials; repeat with a failed connection, dirty draft and pending write.

**Acceptance Scenarios**:
1. Candidate preparation authenticates only against its own backend and discovers its capabilities before committing selection. Failure or cancellation retains the old connection.
2. A successful switch replaces backend-bound clients, caches, module state and live subscriptions. Old responses cannot repopulate the new workspace. Credentials are never transferred to another backend.
3. Unsaved work and pending writes are guarded before switching. No mutation is automatically replayed against a new backend.
4. A switch opens a safe landing page; an old resource identifier is never interpreted against the new backend. Resource URLs carry their backend context.
5. Full API shell paths are preserved. Production connections remain clearly identified. An unavailable backend does not make connection recovery or host-only settings unreachable.

### User Story 3 - Adapt Studio to backend capabilities (Priority: P1)

Studio modules declare the backend operations they require. Capabilities are an availability/compatibility surface, separate from configurable shell features and caller permission.

**Independent Test**: Use authoring-only, runtime-only, dormant, incompatible and unavailable backends and inspect navigation, direct routes and optional actions.

**Acceptance Scenarios**:
1. Registered UI is admitted according to module-owned capability requirements, supported versions, advertised operation relations, permissions and policy.
2. Unsupported, dormant, incompatible, authentication-required, forbidden and connection-failure states retain their meaning and reason.
3. Capability discovery failure never becomes an empty successful capability document. Unknown capabilities do not break supported surfaces.
4. Capabilities refresh after reconnect, explicit refresh and successful backend feature administration.

### User Story 4 - Administer the exact backend (Priority: P1)

Manage features reuses the existing Features module for the named backend. Features retain their settings, dependencies, local drafts, impact review and Apply behavior.

**Independent Test**: Stage enablement and a setting on backend A, change backend, and prove that A's review cannot apply to B. Apply a reviewed change and observe refreshed capabilities.

**Acceptance Scenarios**:
1. Feature catalogs, drafts, reviews and completion messages identify the actual backend and shell; changing the target invalidates an old review.
2. Feature display distinguishes configured enablement, runtime descriptor availability and operational availability, including dormant reasons.
3. Typed per-feature editors, secret masking, unchanged-field preservation, required-dependency cascades and full-state revision-checked Apply remain intact.
4. Personal backend URLs cannot retarget Studio's privileged server-side management integration. Unsupported/read-only administration remains an explicit state.

### Edge Cases

- A selected profile is edited, removed, changed by configuration, or no longer permitted.
- Concurrent profile saves; unavailable preference storage; sign-in redirect or cancellation during candidate preparation.
- Same origin with different shell paths; same resource IDs on different backends; late reads or stream events after switching.
- Capability documents have malformed links, unsupported versions, duplicate identities or dormant reasons.
- Backend feature Apply reloads its shell, rejects activation, conflicts with a later configuration revision, or fails after persistence.
- Settings without descriptors remain owned by their domain API rather than invented generic configuration controls.

## Requirements

### Functional Requirements

- **FR-001**: Provide configured and policy-controlled personal profiles, legacy compatibility, bounded scoped persistence, and separate list/details/create/edit views.
- **FR-002**: Provide a persistent active connection identity, tab-local selection and an accessible quick switcher in sidebar and top-navigation layouts.
- **FR-003**: Prepare and validate a candidate without transferring credentials or changing the active session before success.
- **FR-004**: Guard drafts/writes and invalidate all previous backend-bound state when committing a switch; preserve host-owned state.
- **FR-005**: Bind resource navigation and recoverable state to a backend identity and complete shell URL.
- **FR-006**: Reuse versioned backend capability discovery and module-owned requirements for every affected UI surface.
- **FR-007**: Reuse exact-target feature administration and per-feature settings, preserving draft/review/Apply and revision behavior.
- **FR-008**: Preserve source ownership, host authorization, server-only secrets and privileged-management boundaries.
- **FR-009**: Preserve keyboard access, readable state reasons, light/dark themes and layouts down to 320px.

### Key Entities

- **Backend profile**: A named connection definition and its ownership.
- **Backend session**: One prepared connection, authenticated identity, capability snapshot and lifetime.
- **Capability requirement**: A module's supported backend contract and required operations.
- **Feature administration target**: The exact connection/shell and identity to which a catalog, draft and review belong.

## Success Criteria

- **SC-001**: Configured and personal profile journeys pass with source ownership and identity isolation preserved.
- **SC-002**: Real two-backend journeys prove safe successful, cancelled and failed switches without data or credential crossover.
- **SC-003**: Authoring-only/runtime-only and unavailable/dormant/incompatible cases render the correct admitted surfaces and reasons.
- **SC-004**: A feature-setting Apply affects only the reviewed target and refreshes its capability-derived Studio availability.
- **SC-005**: Affected suites, browser journeys, architecture/maps, package-consumer proof and hosted checks qualify the exact reviewed head and resulting main.

## Assumptions

- The owner-approved design and delivery instruction authorize this work unit; no new program is activated.
- Existing authentication providers and governed preference contracts are reused. Configuration and domain settings keep their existing owners.
- The implementation adds no deployment, release, universal credential forwarding or arbitrary server-side URL proxy.
- Tests follow risk-based cadence; no project-wide TDD cadence is inferred. Regression bite proofs establish behavior where feasible.

# Feature Specification: Reusable Studio authentication and management bridge

**Feature Branch**: `agent/codex/3358-studio-auth-bridge`

**Created**: 2026-10-26

**Status**: Implemented

**Input**: Deliver [#3358](https://github.com/elsa-workflows/elsa-foundation/issues/3358) within
[Program #3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340), following the accepted
[hosting contract](../../docs/reports/studio-hosting-contract.md) and base Hosting implementation #3357.
A custom host should receive Studio's existing user-session and privileged backend relay behavior without copying Web classes.

## User Scenarios & Testing

### User Story 1 — Authorize Studio host control consistently (Priority: P1)

A host developer configures Studio authentication and selects trusted hub paths. Module authors use the existing
public authorization contract; enabled authentication validates the actual backend bearer session even when
the surrounding application has another authenticated identity.

**Independent Test**: Public hosting composition with a recording backend, root/module endpoints and a fake clock.
Exercise disabled/enabled modes, missing/invalid/valid tokens, missing/sufficient permissions and cache transitions.

**Acceptance Scenarios**:

1. **Given** authentication disabled, **When** a Studio management request arrives, **Then** the documented anonymous demo posture remains available.
2. **Given** enabled authentication, **When** a request has no valid backend session, **Then** it receives401; a valid session missing the required permission receives403.
3. **Given** a manage permission, **When** the existing read policy is requested, **Then** manage satisfies read without relying on backend implication expansion.
4. **Given** a token in the query string, **When** its path is an explicitly trusted hub path or child, **Then** it can be read; an unrelated/sibling path accepts bearer by header only.
5. **Given** an application using a sole cookie scheme without an explicit default, **When** Studio is registered before or after it, **Then** ordinary application sign-in/authentication/challenges keep working, while Studio policies still require the backend bearer. Explicit application defaults, multiple-scheme ambiguity and the framework auto-default suppression switch remain authoritative.
6. **Given** cached authenticated/anonymous outcomes, **When** the configured TTL expires or caching is disabled,
   **Then** backend revalidation reflects permission changes/revocation according to the existing bounded-staleness contract.

### User Story 2 — Keep privileged credentials on the server (Priority: P1)

A browser reads Studio-owned backend availability and registry responses. Studio attaches its configured management
key only on privileged server-to-server requests; backend session validation uses the browser bearer alone.

**Independent Test**: Record request paths, headers and counts while exercising status/registry success, incomplete
configuration, rejection, malformed payload and transport failure; inspect browser responses/runtime JS.

**Acceptance Scenarios**:

1. **Given** an absent backend URL or management key, **When** the management bridge is requested,
   **Then** it fails closed with no privileged outbound request. Ordinary session introspection remains a separate user-auth operation.
2. **Given** a configured key, **When** status/registry are read, **Then** only the privileged backend request receives the key; neither user-session introspection nor any browser response does.
3. **Given** a public browser URL and a separate server URL, **When** Studio calls the backend,
   **Then** it uses the server URL while preserving the existing browser-facing response contract.
4. **Given** backend rejection, unavailable transport or unrecognized payload, **When** the bridge responds,
   **Then** it preserves the existing explicit availability states without echoing server secrets.

### User Story 3 — Preserve ownership through shell replacement (Priority: P2)

A developer embeds Studio into a larger host. Authentication/session caches, the clock, options and HTTP factory
retain their root ownership; shell replacement borrows required services without resetting caches or disposing root objects.

**Independent Test**: Public root/shell endpoints and manual framework reload with a recording backend and an owned test clock/filter.

**Acceptance Scenarios**:

1. **Given** a cached valid session, **When** the selected shell reloads, **Then** the root cache remains valid until its configured expiration.
2. **Given** root services borrowed by a shell, **When** that shell drains, **Then** it does not dispose the root-owned clock/factory or capture a request service provider.
3. **Given** changed configuration after registration, **When** runtime settings and policy enforcement are observed,
   **Then** both reflect the installed authentication posture until restart.
4. **Given** host-owned HTTP configuration or a fake clock, **When** public service composition is used,
   **Then** the host can customize them without referencing private implementation types.

### Edge Cases

- Cache capacity remains bounded; non-positive capacity retains the documented fallback, while zero TTL disables caching.
- Indeterminate backend failures do not become cached authentication failures; definitive anonymous outcomes retain the shorter negative TTL.
- Request cancellation remains distinct from backend timeout/failure; no retry layer or new revocation service is introduced.
- Authentication scheme and policy names remain stable for existing modules; missing permission handlers fail closed.
- Trusted query prefixes remain explicit, non-root and segment-aware, with no dependency on Console Stream in Hosting.

## Requirements

### Functional Requirements

- **FR-001**: Common Hosting registration/mapping MUST include the existing Studio bearer gate and management bridge.
- **FR-002**: Enabled/disabled authentication,401/403 distinction, manage-implies-read and bounded cache behavior MUST remain compatible.
- **FR-003**: Only explicitly trusted hub prefixes MAY accept query tokens; header authentication remains available elsewhere.
- **FR-004**: Management keys MUST remain server-only and absent from session introspection/browser projections.
- **FR-005**: Incomplete privileged configuration MUST issue zero privileged outbound calls and preserve existing status/registry response shapes.
- **FR-006**: Root services MUST be borrowed through existing public CShells sharing contracts with explicit ownership/aliases.
- **FR-007**: Stable coarse/read/manage policy names and permission keys MUST live on the existing public Api authorization contract; handlers/cache/client/DTO implementation stays private.
- **FR-008**: Public configuration/HTTP/clock seams MUST support custom hosts and tests without a new production friend grant.
- **FR-009**: Web's superseded authentication/bridge implementation and explicit copied wiring MUST be removed after public adoption.

### Key Entities

Existing backend options, permission requirements and browser DTO shapes are reused. The root session cache stores
hashed bearer identity and bounded definitive outcomes; this extraction introduces no persisted domain model.

## Success Criteria

- **SC-001**: A host using common Hosting calls serves the authentication/bridge matrix with no copied Web implementation.
- **SC-002**: All permitted/denied credential and permission cases match their expected statuses and outbound-call counts.
- **SC-003**: Every secret sentinel remains absent from runtime/browser projections and user-session request headers.
- **SC-004**: Shell replacement preserves root service ownership and cache semantics.
- **SC-005**: Hosting still has no mandatory Nuplane, Theme Store, backend identity or cluster implementation dependency.

## Assumptions

- #3357 supplies the concrete Hosting owner; stacked delivery merges after it. This task does not claim that parent work as its own.
- Cache revocation follows existing TTL/disabled-cache behavior; no immediate distributed revocation promise is added.
- Host configuration is supplied before AddElsaStudio; installed security choices require restart.
- Optional administration/themes, browser ownership and final consumer matrices remain their named later leaves.
- Existing valid behavior tests are preserved through public configuration/HTTP composition. The derived constitution declares no mandatory test-first cadence.
- No release, deployment, publisher activation, new loader or new identity implementation is included.

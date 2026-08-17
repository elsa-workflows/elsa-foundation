# Feature Specification: Foundation Identity Minimal API Migration

**Feature Branch**: `codex/1369-wave3-identity-minimal-apis`

**Created**: 2026-08-16

**Status**: Implemented

**Input**: User description: "Migrate the nine Foundation Identity FastEndpoints registrations to module-owned Minimal APIs while preserving authentication, permission, HTTP/OpenAPI, mixed-host, and unloadability contracts."

## User Scenarios & Testing

### User Story 1 - Existing identity clients continue to work (Priority: P1)

Clients can bootstrap identity, inspect providers, challenge an interactive provider, exchange or refresh tokens, inspect a session, log out, and use the local login page through the same routes and observable contracts.

**Why this priority**: These endpoints establish the host's authentication boundary. A subtle challenge, cookie, redirect, token, or schema drift can lock users out or weaken the trust boundary.

**Independent Test**: Replay the committed FastEndpoints-before HTTP and consumed OpenAPI cases against the migrated host and reject every difference that is not explicitly approved with a rationale.

**Acceptance Scenarios**:

1. **Given** an existing client request, **When** it invokes any of the nine identity operations, **Then** its route, method, binding, status, response, headers, cookie, redirect, challenge, and consumed OpenAPI contract match the approved baseline.
2. **Given** a malformed or unauthenticated request, **When** the operation rejects it, **Then** it preserves the prior failure status and response shape.
3. **Given** a first-party bearer token, **When** it is sent to the interactive token endpoint, **Then** it cannot be exchanged for another token and receives 401.

### User Story 2 - Permission evaluation remains unified (Priority: P1)

Operators can inspect identity-provider capabilities only when the normalized principal has the catalog-owned read permission, an implied permission, or the existing evaluator-level wildcard grant.

**Why this priority**: Route metadata must describe the capability the module owns while all endpoint authoring styles share one Foundation Identity evaluator.

**Independent Test**: Execute the capabilities route and a representative retained FastEndpoints route with anonymous, unrelated, exact, implied, wildcard, and normalized external claims.

**Acceptance Scenarios**:

1. **Given** no authenticated principal, **When** capabilities are requested, **Then** the result is 401; an authenticated principal with an unrelated permission receives 403.
2. **Given** the exact or implied identity-provider read permission, **When** capabilities are requested, **Then** access succeeds.
3. **Given** the administrative wildcard claim, **When** access is evaluated, **Then** access succeeds through the evaluator even though endpoint metadata names only the catalog-owned action.
4. **Given** Minimal API and retained FastEndpoints routes, **When** equivalent principals invoke them, **Then** both use the same evaluator semantics.

### User Story 3 - Hosts can compose and unload both identity owners (Priority: P2)

Hosts can load Foundation Identity API and ASP.NET Core Identity API beside a retained FastEndpoints feature, inspect stable route ownership/security metadata, and release route, authentication, provider, serialization, and disposal references during repeated unload cycles.

**Why this priority**: The authoring migration is incomplete if framework metadata or service-provider state roots a dynamically loaded owner.

**Independent Test**: Materialize real route delegates, auth schemes/provider delegates, source-generated serializers, DI state, and disposal paths for each owner, release them, and observe collection across repeated isolated cycles.

**Acceptance Scenarios**:

1. **Given** a mixed host, **When** both Minimal API owners and an unrelated FastEndpoints route are mapped, **Then** all routes coexist without collision.
2. **Given** any migrated route, **When** its endpoint manifest is inspected, **Then** it has one owner, Minimal API authoring metadata, stable name/tag/operation identity, and one public-or-policy security disposition.
3. **Given** a collectible owner context, **When** route, auth, provider, serializer, DI, and disposal references are released, **Then** repeated weak-reference checks prove collection.

## Edge Cases

- Provider challenge is restricted to configured interactive schemes and preserves provider-not-found and redirect behavior.
- Token exchange ignores any default-scheme bearer identity and authenticates only through configured interactive schemes.
- Refresh binding preserves empty, malformed, expired, revoked, and valid refresh-token outcomes.
- Login preserves JSON/form binding, safe return-URL behavior, invalid credentials, cookies, and source-generated request handling.
- Empty provider lists and anonymous sessions retain their exact response shapes.
- Wildcard compatibility remains evaluator behavior and is never emitted as route policy ownership.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST replace exactly nine FastEndpoints registrations owned by Foundation Identity API and ASP.NET Core Identity API and remove no unrelated registration.
- **FR-002**: The system MUST preserve the approved route/method, request binding, response/schema, status, header, redirect, cookie, challenge, and consumed OpenAPI contracts for all nine operations.
- **FR-003**: Token issuance MUST authenticate only through configured interactive schemes; an already authenticated first-party bearer principal MUST NOT be accepted as token-exchange authority.
- **FR-004**: The capabilities route MUST name only the catalog-owned `identity.providers.read` action in endpoint metadata. Wildcard compatibility MUST remain evaluator-level behavior.
- **FR-005**: Authorization evidence MUST distinguish anonymous 401 from authenticated-but-denied 403 and prove exact, implied, wildcard, and normalized external-provider grants.
- **FR-006**: A representative migrated Minimal API route and retained FastEndpoints route MUST use the same Foundation policy provider and permission evaluator.
- **FR-007**: Every migrated route MUST publish one module owner, Minimal API authoring metadata, stable endpoint name/tag/operation identity, and one public-or-policy security disposition.
- **FR-008**: JSON request and response paths owned by the migrated modules MUST use owner-local source-generated serialization metadata or equivalent non-retaining evidence.
- **FR-009**: Both owners MUST pass repeated collectibility evidence covering endpoint metadata/delegates, authentication schemes and provider delegates, dependency injection, serialization, and disposal.
- **FR-010**: The implementation MUST retain immutable FastEndpoints-before HTTP/OpenAPI fixtures and compare the real migrated host with exact, exhaustively consumed approvals.
- **FR-011**: Production owner projects MUST no longer reference or discover FastEndpoints; test-only FastEndpoints references MAY remain for the coexistence canary and before oracle.
- **FR-012**: Shared identity documentation and test descriptions MUST describe the unified policy/evaluator path and MUST NOT claim a removed FastEndpoints claim-type bridge remains in production.
- **FR-013**: The transition registry MUST remove exactly nine entries, ratcheting the Wave 2 baseline from 143 to 134 registrations and from eight to six owners.

### Key Entities

- **Identity endpoint contract**: Route, method, binding, response, security, ownership, and consumed OpenAPI observations for one identity operation.
- **Interactive authentication scheme**: A configured browser/external scheme permitted to establish the principal used for token issuance.
- **Permission disposition**: One catalog-owned action policy or an explicit public designation attached to a route.
- **Approved compatibility difference**: An exact before/after delta with rationale that must be consumed once and fails if stale or broadened.
- **Collectibility cycle**: One isolated owner load, route/auth/provider/serializer/DI materialization, disposal, and weak-reference collection observation.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Exactly nine baseline registrations and two owner assemblies leave the transition registry; all unrelated entries remain.
- **SC-002**: All committed HTTP and OpenAPI cases pass with zero unapproved or unused compatibility differences.
- **SC-003**: 100% of anonymous, denied, exact, implied, wildcard, normalized, and bearer-to-token security cases produce the expected outcome.
- **SC-004**: Both identity owners pass at least three consecutive full collectibility cycles with only weak references retained.
- **SC-005**: Identity tests, architecture/security/collectibility tests, relevant backend E2E suites, full build, maps freshness, formatting, and diff review complete successfully.

## Assumptions

- Foundation Identity's existing dynamic policy provider, claims normalizer, implication catalog, and evaluator remain the security source of truth.
- Existing identity application services and frozen FastEndpoints fixtures are behavior oracles; this wave changes endpoint authoring and module composition, not identity semantics.
- The administrative wildcard remains an evaluator-level compatibility grant.
- Framework §2.24 and Elsa §E2.9 remain draft/provisional and are review inputs rather than ratified exceptions.

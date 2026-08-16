# Feature Specification: Wave 4 Agent REST and SSE API Migration

**Feature Branch**: `codex/1370-wave4-agent-api`

**Created**: 2026-08-16

**Status**: Implementation and control-room integration gates complete; independent final review pending

**Input**: Issue #1370: replace the eleven Elsa Agent API FastEndpoints registrations with explicit Minimal API mappings while preserving HTTP, OpenAPI, authorization, and SSE contracts.

## User Scenarios & Testing

### User Story 1 - Preserve Agent HTTP and OpenAPI contracts (Priority: P1)

As an Agent API consumer, I can continue to bootstrap, manage sessions, post and cancel turns,
manage proposals, submit feedback, and audit activity without an unapproved route, binding, JSON,
status, header, or consumed OpenAPI change.

**Independent Test**: Compare the immutable FastEndpoints-before HTTP and consumed OpenAPI fixtures
with a real host containing the Minimal API mapper for all eleven registrations.

### User Story 2 - Use shared permission authorization (Priority: P1)

As a host operator, I can authorize Agent routes with Foundation Identity policies regardless of
whether another route in the same host still uses FastEndpoints. Exact, implied, wildcard,
resource, and tenant behavior is explicit and fail-closed.

**Independent Test**: Exercise anonymous, authenticated-denied, exact, implied, wildcard, resource,
and tenant principals against Agent Minimal API routes and a representative FastEndpoints route.

### User Story 3 - Preserve streaming and lifecycle behavior (Priority: P1)

As a client consuming Agent events, I receive the existing SSE framing and response headers,
request cancellation stops the stream, in-flight work is disposed, and repeated dynamic owner
load/map/unload cycles do not retain Agent route, DI, authentication, or serializer state.

**Independent Test**: Run framing/backpressure and cancellation tests plus three real route-publication
cycles through a collectible `AssemblyLoadContext` with weak-reference evidence.

## Acceptance Scenarios

1. **Given** an anonymous request to a protected Agent route, **when** it is handled, **then** the
   response is `401`.
2. **Given** an authenticated caller without the route permission, **when** it requests the route,
   **then** the response is `403`.
3. **Given** an exact, implied, or wildcard grant, **when** the caller requests an eligible route,
   **then** the shared evaluator authorizes it; resource and tenant mismatches remain denied.
4. **Given** the frozen eleven-route FastEndpoints observations, **when** the Minimal API host is
   compared, **then** all HTTP and consumed OpenAPI projections match with no blanket approval.
5. **Given** an active Agent SSE stream, **when** the request is cancelled, **then** enumeration and
   disposal complete without retaining the previous request or service generation.
6. **Given** a host with a transitional FastEndpoints route, **when** both route models are mapped,
   **then** each remains independently callable and uses the same permission evaluator.

## Functional Requirements

- **FR-001**: The system MUST map exactly eleven Agent registrations through an explicit
  `IEndpointRouteBuilder` seam and remove their production FastEndpoints endpoint adapters.
- **FR-002**: The system MUST preserve each route template, HTTP method, binding, response status,
  JSON shape, headers, content type, and consumed OpenAPI operation identifier from the immutable
  before fixtures.
- **FR-003**: The system MUST preserve the current SSE event framing and cancellation behavior.
  Heartbeat and resume semantics are not present in the consumed baseline and MUST NOT be invented
  as an unreviewed contract change.
- **FR-004**: Each endpoint MUST carry one Agent owner, Minimal API authoring metadata, and exactly
  one security disposition.
- **FR-005**: Agent routes MUST express `agent.use`, `agent.proposals`, and `agent.audit` through
  standard ASP.NET Core authorization metadata and one Foundation Identity evaluator. Wildcard is
  evaluator-level compatibility, not route ownership metadata.
- **FR-006**: The Agent permission contributor MUST own the three action permissions and explicitly
  review the implication from `agent.proposals` to `agent.use`.
- **FR-007**: The migration MUST use owner-local source-generated JSON contexts for route response
  serialization and SSE payload serialization.
- **FR-008**: Repeated route, SSE completion/cancellation, DI, authentication, serializer, and
  disposal tests MUST provide collectible-context evidence; process memory observations alone are
  insufficient.
- **FR-009**: The transition ratchet MUST remove exactly eleven Agent registrations and retain
  FastEndpoints support for unrelated transitional owners.
- **FR-010**: The work MUST remain independently revertible and MUST NOT migrate other Elsa modules,
  redesign public routes, replace HTTP/JSON, or change identity-provider behavior.

## Key Entities

- **Agent endpoint contract**: route, method, binding, response, OpenAPI, security, and SSE
  observations for one of the eleven registrations.
- **Agent permission contribution**: owner-scoped catalog actions and their reviewed implications.
- **Agent route generation**: the published route and dependency set for one dynamically loadable
  Agent owner context.
- **Lifecycle evidence**: repeated weak-reference observations over route, endpoint metadata,
  DI, authentication delegates, serializer context, and disposal state.

## Success Criteria

- **SC-001**: Exactly eleven Agent HTTP cases and eleven consumed OpenAPI operations compare with
  zero unapproved differences.
- **SC-002**: Anonymous, denied, exact, implied, wildcard, resource, tenant, and mixed-framework
  authorization cases pass through the same evaluator.
- **SC-003**: SSE headers and framing match the frozen contract; cancellation completes enumeration
  and disposal without a leaked request generation.
- **SC-004**: Three repeated real route-publication cycles provide collectible-context evidence for
  the Agent assembly, mapper, endpoints, DI provider, auth/metadata delegates, serializer context,
  and disposal path.
- **SC-005**: The transition inventory drops from 134 to 123 registrations, leaving five owners and
  no Agent owner entries; unrelated FastEndpoints coexistence coverage remains.

## Assumptions and Scope

- ASP.NET Core Minimal APIs and Foundation Identity are the accepted target from ADR 0068 and the
  first-party REST consolidation program.
- Existing Agent services and public DTOs are authoritative; this work changes endpoint authoring,
  not domain behavior or protocol shape.
- `agent.proposals` implies `agent.use`; `agent.audit` is independent. The administrative wildcard
  remains a grant accepted by the evaluator.
- Heartbeat, resume tokens, and a new SSE backpressure protocol require a separate reviewed contract
  change because they are absent from the consumed FastEndpoints baseline.

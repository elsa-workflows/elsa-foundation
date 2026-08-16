# Feature Specification: Wave 1 Small and Read-Oriented REST API Migration

**Feature Branch**: `codex/1367-wave1-minimal-apis`

**Created**: 2026-08-16

**Status**: Implemented

**Input**: Issue #1367: migrate the six small/read-oriented API owners and exactly eight concrete FastEndpoints registrations to explicit ASP.NET Core Minimal API mapping seams.

## User Scenarios & Testing

### User Story 1 - Preserve the eight public HTTP contracts (Priority: P1)

As a management API consumer, I can call the capabilities, attention, expression, JavaScript, and dashboard routes after the authoring migration without an unapproved route, method, binding, JSON, status, ProblemDetails, content-type, or consumed OpenAPI change. Legacy operation IDs, host-application tags, and the runtime-JavaScript `RequestModel` schema identifier remain stable.

**Independent Test**: Compare the committed immutable FastEndpoints-before HTTP and consumed-OpenAPI fixtures with a real eight-route Minimal API host through `Elsa.Api.Compatibility.Testing`; run representative success, query-error, malformed-body, missing-body, and valid-but-blank-body cases.

**Acceptance Scenarios**:

1. **Given** an enabled Wave 1 host, **when** each registered route is discovered, **then** the exact eight route/method pairs are present once.
2. **Given** a valid request, **when** it is sent to a migrated route, **then** the response status, JSON shape, content type, and service interaction match the frozen baseline.
3. **Given** an invalid query or body, **when** the route handles it, **then** the legacy status and error representation are retained unless the explicit JavaScript metadata correction is approved.

### User Story 2 - Preserve permission authorization across endpoint models (Priority: P1)

As a host operator, I can use the same Foundation Identity policy provider and evaluator for Minimal API routes as for transitional FastEndpoints routes, including exact permissions, implied permissions, and the administrative wildcard.

**Independent Test**: Exercise anonymous, authenticated-without-permission, exact, implied, and wildcard principals against representative migrated routes and one transitional route in the same host.

**Acceptance Scenarios**:

1. **Given** an anonymous caller, **when** a protected Wave 1 route is requested, **then** the response is `401`.
2. **Given** an authenticated caller without the required permission, **when** a protected route is requested, **then** the response is `403`.
3. **Given** a caller with the exact, implied, or retained wildcard grant, **when** the route is requested, **then** the shared evaluator authorizes it.

### User Story 3 - Compose and unload each migrated owner explicitly (Priority: P2)

As a modular host maintainer, I can compose each migrated owner through an explicit `IEndpointRouteBuilder` seam and verify repeated collectible-context route, dependency-injection, serialization, and disposal evidence.

**Independent Test**: Map each owner in a test host, inspect ownership/authoring/security metadata, invoke its route, dispose the host, and repeat the weak-reference collection check.

**Acceptance Scenarios**:

1. **Given** a migrated owner, **when** its mapper is called, **then** every endpoint has one module owner, Minimal API authoring metadata, and exactly one security disposition.
2. **Given** repeated load/map/request/dispose/unload cycles, **when** weak references are checked, **then** every owner context is collectible or the owner-specific blocking retention is reported without weakening the gate.
3. **Given** the old FastEndpoints registration registry, **when** the wave completes, **then** exactly eight entries are removed and no owner-local FastEndpoints dependency remains unused.

## Edge Cases

- Attention retains repeated `contributorId` query values and maps malformed contributor selection to its existing plain-text `400` response.
- Dashboard retains ISO-8601, bucket, tenant, and boolean query validation and plain-text `400` responses.
- JavaScript execution and rendering retain empty-input `400` responses and generic execution/rendering `500` JSON responses.
- A protected route never uses `AllowAnonymous`; public capability documents remain permission-neutral only in their response contents, not in route access.
- OpenAPI metadata uses stable ASP.NET Core response metadata and does not introduce a module-specific endpoint DSL.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST replace exactly the eight listed FastEndpoints registrations with six explicit module-owned Minimal API mapping seams.
- **FR-002**: The system MUST preserve the exact route and HTTP method contract for every migrated registration.
- **FR-003**: The system MUST preserve request binding, JSON serialization, response status, ProblemDetails/plain-text behavior, response content types, and consumed OpenAPI metadata, except for the two exact JavaScript OpenAPI projections recorded in the shared approval registry and requiring reviewer approval.
- **FR-004**: Each migrated endpoint MUST carry one module owner, Minimal API authoring metadata, and exactly one security disposition.
- **FR-005**: Protected routes MUST use Foundation Identity policy metadata and the shared evaluator; provider-specific claim mapping MUST remain outside endpoint handlers.
- **FR-006**: `ApiCapabilitiesRead` and `expressions.read` MUST remain required; wildcard-only routes MUST gain module-owned action permissions without becoming public.
- **FR-007**: The wave MUST prove anonymous `401`, authenticated denial `403`, exact, implied, and wildcard authorization behavior.
- **FR-008**: The wave MUST prove Minimal API and transitional FastEndpoints coexistence in one host.
- **FR-009**: The wave MUST retain repeated collectible-context evidence for all six owners across routing, DI, serialization, and disposal.
- **FR-010**: The wave MUST remove exactly eight Wave 0 registry entries and owner-local FastEndpoints references made unused by the migration.
- **FR-011**: The wave MUST remain independently revertible and MUST NOT migrate owners outside #1367.
- **FR-012**: The wave MUST preserve all eight legacy operation IDs, the host-application OpenAPI tag, and the runtime-JavaScript `RequestModel` schema identifier using standard ASP.NET Core metadata.
- **FR-013**: The wave MUST record the legacy JavaScript `204` and omitted-error-status metadata as explicit compatibility exceptions and MUST NOT claim zero OpenAPI differences until those exceptions are reviewed.
- **FR-014**: The wave MUST commit immutable FastEndpoints-before HTTP/OpenAPI fixtures, compare all consumed canonical response/request/schema projections through the shared comparer, and fail on any unapproved or unused Wave 1 approval.

### Key Entities

- **Wave 1 owner mapper**: A module-owned `Map*Api(IEndpointRouteBuilder)` seam and `IWebShellFeature` adapter.
- **Endpoint contract observation**: Immutable route, method, binding, JSON, status, error, content-type, and OpenAPI facts for a legacy registration.
- **Permission catalog entry**: A stable action permission contributed by exactly one owning module and consumed through Foundation Identity policy metadata.
- **Collectibility evidence**: Repeated weak-reference observations showing that route, DI, serializer, and disposal references do not retain a migrated module.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All eight registrations are replaced, with zero first-party FastEndpoints registrations for the six Wave 1 owners.
- **SC-002**: Every frozen compatibility case passes with zero unapproved HTTP or consumed OpenAPI differences.
- **SC-003**: All protected-route authorization cases return the expected `401`/`403`/`200` outcomes through one evaluator.
- **SC-004**: Every published Wave 1 endpoint has exactly one owner, one Minimal API authoring marker, and one security disposition.
- **SC-005**: All six owner collectibility suites pass their repeated weak-reference checks.

## Assumptions

- ASP.NET Core Minimal APIs and Foundation Identity are the accepted target from ADR 0068 and issue #1342.
- The legacy public route and DTO contracts are the baseline; contract redesign is out of scope. The two inaccurate JavaScript OpenAPI declarations are a deliberate, review-gated correction.
- Existing domain/application services remain unchanged and provide business validation.
- The administrative wildcard remains a grant and is not itself a catalog-owned permission.
- Dynamic workflow-authored routes and host-owned routes remain owned by #1366 and #1365.

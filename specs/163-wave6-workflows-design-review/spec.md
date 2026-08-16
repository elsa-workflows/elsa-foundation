# Feature Specification: Wave 6 Workflows Design API Review Corrections

**Feature Branch**: `codex/1372-wave6-workflows-design-minimal-apis`

**Created**: 2026-08-16

**Status**: Implementation

**Input**: Issue #1372 review round 1: close the compatibility, authorization, lifecycle, semantic-test, documentation, and backend-E2E evidence gaps in the Workflows Design API Minimal API migration.

## User Scenarios & Testing

### User Story 1 - Preserve every design API contract (Priority: P1)

As a workflow-design API consumer, I can use all 27 existing routes with the same successful and failed requests, binding rules, response bodies, headers, content types, filtering, paging, and concurrency behavior after the migration.

**Why this priority**: Any unverified wire change breaks existing Studio and headless clients.

**Independent Test**: Compare a real FastEndpoints-era capture host and a real Minimal API host using immutable HTTP and consumed OpenAPI evidence, including success, binding, domain-error, query, and concurrency cases for every route.

**Acceptance Scenarios**:

1. **Given** the frozen before capture and the mapped after host, **when** all 27 route cases run, **then** status, body, headers, content type, binding, and error projections match exactly unless a two-sided, consumed approval is explicitly recorded.
2. **Given** valid and invalid JSON, missing bodies, malformed JSON, and unsupported content types, **when** a client sends each request, **then** the same transport status and ProblemDetails/domain error shape are returned.
3. **Given** paging/filtering and version-concurrency inputs, **when** they are sent through both hosts, **then** precedence, empty values, conflict statuses, and headers remain identical.

### User Story 2 - Apply one secure authorization contract (Priority: P1)

As a host operator, I can authorize Workflows Design routes through the shared Foundation Identity evaluator, with catalog actions owned by the module and wildcard/implied grants handled only by the evaluator.

**Why this priority**: Incorrect policy metadata can expose workflow definitions or block legitimate design clients.

**Independent Test**: Exercise anonymous, denied, exact, implied, wildcard, external-untrusted, tenant/resource allow-deny, and retained FastEndpoints canary principals against real mapped routes.

**Acceptance Scenarios**:

1. **Given** no principal or an untrusted external principal, **when** a protected route is called, **then** the response is respectively 401 or 403.
2. **Given** exact, implied, or evaluator wildcard grants with a matching tenant/resource, **when** the route is called, **then** it succeeds; mismatched tenant/resource claims remain denied.
3. **Given** a retained FastEndpoints route in the same host, **when** it is called with the same principals, **then** the evaluator produces the same authorization outcome.

### User Story 3 - Retain lifecycle, serialization, and semantic behavior (Priority: P1)

As a platform maintainer, I can load and unload the design owner repeatedly without retaining route, DI, authentication, stores, providers, OpenAPI, or source-generated serializer state, while design handlers preserve their prior semantic behavior.

**Why this priority**: A migration that passes a shallow route check can leak generations or silently remove business behavior from expression tooling and workflow lifecycle operations.

**Independent Test**: Run three real mapped cycles through authorization, stores/adapters, provider calls, OpenAPI generation, source-generated serialization, DI disposal, and weak-reference checks; run restored semantic tests for provider outcomes and lifecycle conflicts/errors.

**Acceptance Scenarios**:

1. **Given** three successive owner generations, **when** each mapped route is invoked, OpenAPI is generated, services are disposed, and the generation is unloaded, **then** all owner delegates, serializers, services, and metadata become collectible.
2. **Given** empty, failed, cached, canceled, and denied expression-tooling providers, **when** the corresponding operations run, **then** outcome states, revisions, headers, and provider call counts match the established behavior.
3. **Given** promotion preflight, promotion, synthetic draft identifiers, and permanent deletion outcomes, **when** each semantic case runs, **then** nonmutation, 404, 409, 501, and 500 behavior remains unchanged.

### Edge Cases

- Null, missing, empty, malformed, and non-JSON bodies must retain exact binding status/content type.
- Route values override conflicting body identifiers exactly as before; absent query values remain distinct from present-but-empty values.
- Wildcard grants must not become endpoint-owned permission metadata; unused or mutation approvals must fail the compatibility comparer.
- Resource and tenant claims must deny access when absent or mismatched, including for implied and wildcard grants.
- OpenAPI operation IDs, tags, parameters, request bodies, responses, and security requirements must be consumed and compared, not merely generated and ignored.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST include source-generated metadata for every request and response type used by the 27 mapped routes, including `PreflightDraftPromotion`.
- **FR-002**: The system MUST express only each catalog-owned action in endpoint permission metadata; wildcard and implication compatibility MUST remain evaluator behavior.
- **FR-003**: The system MUST retain an immutable FastEndpoints-era capture harness and fixtures for all 27 routes, with reproducible source/provenance hashes and representative success, binding, ProblemDetails/domain-error, paging/filtering, concurrency, header, and content-type evidence.
- **FR-004**: The compatibility comparer MUST reject unused approvals, one-sided approvals, and fixture mutations; no post-hoc volatile-header normalization may hide a difference.
- **FR-005**: The system MUST test 401, 403, exact, implied, evaluator wildcard, external-untrusted, tenant/resource allow-deny, and retained FastEndpoints authorization through one evaluator.
- **FR-006**: Consumed OpenAPI comparison MUST cover stable operation IDs, owner-local tags, route parameters, request bodies, responses, and security requirements for all 27 routes.
- **FR-007**: Three real collectible cycles MUST invoke mapped delegates and exercise authorization, stores/adapters/providers, OpenAPI generation, source-generated serialization, DI disposal, and weak-reference collection.
- **FR-008**: Existing expression-tooling and lifecycle semantic coverage MUST be restored without reintroducing production FastEndpoints dependencies.
- **FR-009**: The Workflows Design backend E2E scenario MUST run against a rebuilt Workbench and fresh database, with command and result recorded in the work-unit report.
- **FR-010**: The work MUST preserve exactly 27 removed Workflows Design FastEndpoints registrations, update the executable ratchet/maps, and leave unrelated transitional owners intact.
- **FR-011**: The work MUST record the review reconciliation against ADR 0068 and the owner migration report, with import ordering and format checks passing.

### Key Entities

- **Route evidence case**: one route/method/input outcome with exact status, body, headers, content type, and source provenance.
- **Consumed OpenAPI operation**: operation ID, tag, parameters, body, responses, and security requirements consumed by the compatibility checker.
- **Authorization matrix case**: principal claims, tenant/resource context, authoring model, expected status, and evaluator grant classification.
- **Lifecycle generation**: mapped endpoint delegate, metadata, DI scope, serializer context, provider/store adapters, disposal state, and weak references for one load/unload cycle.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All 27 routes have immutable before and real after HTTP evidence with zero unapproved differences across success, errors, binding, query, headers, and concurrency cases.
- **SC-002**: All 27 consumed OpenAPI operations compare with consumed IDs/tags/parameters/bodies/responses/security and zero unused or one-sided approvals.
- **SC-003**: The complete authorization matrix, including a retained FastEndpoints canary, passes with exact 401/403/allow/deny outcomes.
- **SC-004**: Three mapped generations complete authorization, OpenAPI, serialization, DI disposal, and weak-reference collection checks without retaining owner state.
- **SC-005**: Restored expression/lifecycle semantic tests pass, including provider empty/failure/cache, preflight nonmutation/conflicts/synthetic IDs, and permanent-delete 404/409/501/500 outcomes.
- **SC-006**: Rebuilt Workbench backend E2E design flow passes against a fresh database with a recorded command, environment, and result.

## Assumptions

- Existing FastEndpoints remains available for unrelated transitional owners and for the retained same-evaluator canary only; no Workflows Design production dependency is reintroduced.
- The existing issue #1372 baseline commit is immutable; any additional before evidence is added in a new baseline-first correction commit before implementation changes.
- ADR 0068 remains the accepted architecture decision; its contract is reconciled in the work-unit report rather than amended.
- Workbench E2E setup may use the repository's documented fresh SQLite deployment process; no shared user database is reused.

# Feature Specification: Wave 2 Bounded API Migration

**Feature Branch**: `codex/1368-wave2-minimal-apis`

**Created**: 2026-08-16

**Status**: Draft

**Input**: User description: "Migrate the 13 bounded FastEndpoints registrations owned by BPMN Interchange, Modularity API, Execution Evidence, and Elsa 3 Import to module-owned Minimal APIs while preserving HTTP/OpenAPI, tenant, security, lifecycle, and unloadability contracts."

## User Scenarios & Testing

### User Story 1 - Interchange and import clients retain their workflows (Priority: P1)

Clients can analyze, import, and export BPMN XML and can run the Elsa 3 reusable-activity import workflow through the same routes, payloads, diagnostics, pagination, polling, status codes, and media types after the authoring transition.

**Why this priority**: These routes carry XML, multipart/stream-like uploads, JSON plans, and long-running import state. A wire regression would break migration and authoring clients immediately.

**Independent Test**: Run the committed FastEndpoints-before HTTP/OpenAPI cases against the migrated host for all eight interchange/import operations and compare observations with no unapproved differences.

**Acceptance Scenarios**:

1. **Given** a valid BPMN XML document, **When** a caller analyzes, imports, or exports it, **Then** the same JSON/XML result, status, headers, and diagnostics are returned as before.
2. **Given** a valid Elsa 3 collection upload and a caller's normalized tenant/user scope, **When** the caller analyzes, selects, applies, or polls its status, **Then** the same location, pagination, idempotency, ProblemDetails, and scope isolation behavior is preserved.
3. **Given** malformed XML, JSON, payload, plan, or selection input, **When** the request is submitted, **Then** the prior error status, media type, and diagnostic shape are preserved.

### User Story 2 - Module management and execution evidence remain safe (Priority: P2)

Operators can list/apply module configuration and read/delete execution evidence using the same routes and cursor/long-poll behavior while explicit module-owned permissions enforce authentication, exact grants, manage-implies-read, wildcard compatibility, and tenant isolation.

**Why this priority**: These endpoints expose host configuration and runtime diagnostics. The migration must not widen either capability or evidence visibility while replacing the wildcard-only transition checks.

**Independent Test**: Exercise the five module/evidence operations in a real authorization-enabled host using anonymous, denied, exact, implied, wildcard, normalized, and cross-tenant identities, then compare their HTTP/OpenAPI observations with the before baseline.

**Acceptance Scenarios**:

1. **Given** a caller without authentication, **When** it invokes any protected operation, **Then** it receives 401; a caller with an unrelated permission receives 403.
2. **Given** `module-management.manage`, **When** the caller lists or applies module configuration, **Then** list and apply succeed; `module-management.read` permits only list.
3. **Given** execution-evidence read and delete/manage permissions, **When** the caller reads, polls, correlates, or deletes evidence, **Then** only the catalog-owned exact or implied capabilities succeed and wildcard remains compatible.
4. **Given** evidence or import state in another tenant or user scope, **When** a caller supplies its own normalized identity, **Then** the resource is not disclosed and the prior not-found/isolation result is preserved.

### User Story 3 - Hosts can unload migrated owners and coexist during transition (Priority: P3)

Hosts can load all four migrated owners alongside an unrelated FastEndpoints route, inspect one owner and authoring model per route, and release route/DI/serializer/disposal references so repeated collectible-context checks pass.

**Why this priority**: Minimal API adoption is only complete when the module boundary is explicit and the former framework dependency no longer roots dynamically loaded owners.

**Independent Test**: Build a mixed host containing the four mappers and one unrelated FastEndpoints route, assert the exact 13-route manifest and permission ownership, then repeat route, DI, serializer, and disposal release cycles.

**Acceptance Scenarios**:

1. **Given** a mixed host, **When** the four mappers and an unrelated FastEndpoints feature are mapped, **Then** all routes operate without collisions and share the Foundation Identity evaluator.
2. **Given** every migrated route, **When** its runtime metadata is inspected, **Then** it has exactly one module owner, Minimal API authoring metadata, and exactly one Foundation permission disposition.
3. **Given** a collectible owner context, **When** route delegates, service providers, serializer options, and host/disposal references are released, **Then** the owner is collected in repeated clean cycles.

## Edge Cases

- Empty or missing BPMN XML, invalid process/node identifiers, and malformed JSON retain their existing 400/error media type behavior.
- Import upload size, expiration, tenant/user mismatch, plan drift, dependency closure, idempotency collision, and persistence failures retain their existing status and diagnostic contracts.
- Evidence requests with missing workflow/correlation identifiers, invalid cursors, zero/negative/over-limit wait values, terminal pages, and cancellation preserve polling and pagination behavior.
- Module apply revision conflicts, invalid feature identifiers, and unexpected service failures preserve conflict, client-error, and server-error contracts.
- Authorization uses normalized claims and distinguishes anonymous 401, unrelated 403, exact permissions, implied permissions, wildcard compatibility, and resource-level denial.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST replace exactly 13 concrete FastEndpoints registrations owned by the four named assemblies with explicit module-owned Minimal API mappings and remove no unrelated registrations.
- **FR-002**: The system MUST preserve every baseline route/method identity, request binding, response body/schema, status code, media type, relevant headers, error/ProblemDetails shape, pagination, polling, and OpenAPI operation contract.
- **FR-003**: BPMN interchange MUST preserve XML document handling and analyze/import/export diagnostics; Elsa 3 import MUST preserve upload, JSON analysis/selection/apply, location, idempotency, status, and bounded pagination behavior.
- **FR-004**: Execution evidence MUST preserve workflow and correlation reads, cursor/long-poll semantics, terminal pages, delete behavior, and validation errors.
- **FR-005**: Import and execution-evidence resources MUST continue to apply the normalized tenant/user scope before returning or mutating state.
- **FR-006**: Modularity list MUST require the catalog-owned `module-management.read` permission and apply MUST require `module-management.manage`; the catalog MUST preserve manage-implies-read.
- **FR-007**: Execution Evidence MUST contribute catalog-owned read and delete/manage permissions with explicit implication behavior. Each endpoint MUST name only its catalog-owned action permission; wildcard compatibility is an evaluator-level grant proven by authorization tests and MUST NOT be represented as an endpoint any-permission policy or catalog ownership evidence.
- **FR-008**: Every migrated route MUST publish exactly one module ownership metadata record, Minimal API authoring metadata, and one Foundation permission security disposition; each permission MUST resolve to exactly one active contributor.
- **FR-009**: Authorization tests MUST prove anonymous 401, unrelated 403, exact, implied, wildcard, normalized identity, and resource/tenant isolation outcomes for the affected operations.
- **FR-010**: Production owner projects MUST no longer reference or discover FastEndpoints endpoint bases or unused owner-local FastEndpoints dependencies; FastEndpoints may remain only in test fixtures needed to prove coexistence and baseline behavior.
- **FR-011**: The migration MUST prove repeated collectible route, dependency-injection, serializer, and disposal lifecycles for all four owners without blanket waivers.
- **FR-012**: The implementation MUST retain the immutable FastEndpoints-before HTTP/OpenAPI evidence and compare the real migrated host through `Elsa.Api.Compatibility.Testing`; unapproved differences MUST fail.
- **FR-013**: The migration MUST remove exactly the 13 matching transition-registry entries and ratchet the registry from 156 to 143 after Wave 1 is applied, without stale or generic waivers.

### Key Entities

- **Endpoint contract**: A route/method, binding, response/error, OpenAPI, ownership, and permission disposition captured before and after authoring migration.
- **Module permission**: A catalog-owned action key and its implication relationships used by Foundation Identity evaluation.
- **Execution evidence page**: Ordered records returned by workflow/correlation query with cursor, terminal, and polling semantics.
- **Import operation scope**: The normalized tenant/user identity that owns an uploaded collection and its analysis, selection, receipt, and status resources.
- **Collectibility cycle**: One isolated owner load, route/DI/serializer/disposal release, and weak-reference collection observation.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All 13 baseline registrations are removed and exactly 13 explicit Minimal API routes are present for the four owners, with no unrelated registration changes.
- **SC-002**: Every committed HTTP and consumed OpenAPI compatibility case passes with zero unapproved differences across repeated captures.
- **SC-003**: 100% of authorization cases for anonymous, denied, exact, implied, wildcard, normalized, and cross-scope identities produce the expected outcomes.
- **SC-004**: All four owners pass repeated route, DI, serializer, and disposal collectibility cycles.
- **SC-005**: The affected module tests, compatibility tests, architecture tests, maps check, relevant backend E2E suites, and full build complete successfully, or any unrelated #1323 nightly failure is separately identified and not attributed to this wave.

## Assumptions

- Existing CShells `IWebShellFeature`, ASP.NET Core endpoint routing, Foundation Identity policy/catalog, and compatibility/collectibility test infrastructure are reused.
- Wave 0 inventory hardening and Wave 1 migrations land before this wave's final ratchet; this branch validates the pre-Wave-1 151-entry registry, and the integration rebase ratchets the combined registry from 156 to 143.
- Existing module services and domain tests remain the behavior source of truth; this work changes HTTP authoring and composition only.
- The existing administrative wildcard grant remains an evaluator-level compatibility grant and is not itself a module permission contributor or endpoint policy operand.
- Backend E2E execution uses a rebuilt Workbench and fresh database as required by the repository runner.

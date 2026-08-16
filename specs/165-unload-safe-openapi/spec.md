# Feature Specification: Unload-Safe OpenAPI Boundary

**Feature Branch**: `codex/1392-unload-safe-openapi-boundary`

**Created**: 2026-08-16

**Status**: In progress — implementation for #1392

**Program Goal**: First-party REST API Consolidation (#1342), prerequisite #1392

**Input**: Define an unload-safe OpenAPI contract boundary for collectible Elsa modules so dynamic modules can publish documented HTTP endpoints without API documentation retaining their load contexts.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reload a documented module safely (Priority: P1)

As an operator, I can load, replace, and unload a module that exposes documented HTTP endpoints without API documentation retaining the retired module generation.

**Why this priority**: Collectible module replacement is a core runtime guarantee. The current API documentation path prevents two REST-migration waves from meeting that guarantee.

**Independent Test**: Load a representative module, publish its documented endpoints, generate the real API description and OpenAPI document, replace or remove the module, and prove the retired load context and representative contract types become unreachable for three consecutive cycles.

**Acceptance Scenarios**:

1. **Given** a collectible module whose endpoints expose module-owned request and response contracts, **When** the host generates API documentation and then unloads the module, **Then** no host-owned documentation object retains the retired module generation.
2. **Given** a new module generation replaces an active generation, **When** documentation is requested during and after replacement, **Then** clients observe one complete accepted generation and never a mixture of old and new operations.
3. **Given** a candidate generation cannot publish an unload-safe documentation contract, **When** activation is attempted, **Then** the candidate is rejected with an owner-aware diagnostic and the previous generation remains available.

---

### User Story 2 - Preserve the public API contract (Priority: P2)

As an API consumer, I receive the same HTTP and OpenAPI contract before and after the unload-safety boundary is introduced.

**Why this priority**: Unloadability must not be obtained by weakening schemas, replacing meaningful contracts with opaque objects, or changing established routes and security declarations.

**Independent Test**: Compare immutable before evidence with the real host after implementation for operation identities, paths, methods, request and response schemas, status codes, security requirements, and documented headers; require an explicit approval for every intentional difference.

**Acceptance Scenarios**:

1. **Given** a documented endpoint with a typed request or response, **When** the host publishes its OpenAPI document, **Then** the public schema remains as specific as the accepted before contract without exposing a live type from the collectible generation to host-lifetime caches.
2. **Given** endpoints from static and dynamic owners coexist, **When** a document is generated, **Then** each operation appears exactly once with its established identity, route, method, tags, security requirements, and response dispositions.
3. **Given** a route is replaced by a new generation, **When** a client requests the document, **Then** the document reflects the same generation boundary used for request routing.

---

### User Story 3 - Diagnose unsafe documentation metadata (Priority: P3)

As a module author, I receive a deterministic diagnostic when endpoint documentation would cross the collectible-to-host lifetime boundary unsafely.

**Why this priority**: A fail-closed authoring rule prevents future modules from silently reintroducing retention and makes the boundary reviewable without repeating a GC investigation.

**Independent Test**: Register deliberately unsafe request, response, transformer, and operation metadata and verify validation identifies the module, generation, endpoint, metadata kind, and offending contract identity before publication.

**Acceptance Scenarios**:

1. **Given** an endpoint description contains a host-retained reference to a collectible module artifact, **When** the candidate manifest is validated, **Then** publication fails before the artifact becomes visible and the diagnostic identifies the exact owner and endpoint.
2. **Given** an endpoint uses only approved stable documentation artifacts, **When** validation runs, **Then** the endpoint is accepted without requiring reflection fallback or private cache manipulation.
3. **Given** the documentation subsystem cannot safely represent a contract, **When** activation is attempted, **Then** the system fails closed rather than omitting the operation or publishing an untyped replacement silently.

### Edge Cases

- Documentation is generated concurrently with module replacement or removal.
- Two generations declare the same operation identity or route while one is being drained.
- A request or response graph contains nested generic, collection, dictionary, enum, nullable, inheritance, or polymorphic contracts owned by the collectible module.
- Endpoint metadata includes delegates, transformers, attributes, validation metadata, or examples whose declaring types belong to the collectible module.
- The same stable contract is used by static and dynamic endpoints.
- A module unloads before any document has been generated, after one document, and after repeated document generation.
- Document generation is cancelled or fails partway through a candidate generation.
- An external framework version changes its internal caching behavior; correctness must not depend on private cache clearing, time-based eviction, or forced garbage collection in production.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST define one explicit ownership boundary between collectible endpoint implementations and host-lifetime API documentation artifacts.
- **FR-002**: The boundary MUST preserve the accepted public HTTP and OpenAPI contract, including paths, methods, operation identities, tags, request and response schemas, status codes, headers, content types, and security requirements.
- **FR-003**: Host-lifetime documentation state MUST NOT retain a collectible module generation, its assembly, its contract types, its endpoint delegates, its dependency provider, or its serialization metadata after that generation is retired.
- **FR-004**: Documentation publication and replacement MUST be generation-aware and atomic from the perspective of new document requests.
- **FR-005**: A failed candidate publication MUST leave the previous accepted endpoint and documentation generation available.
- **FR-006**: Validation MUST reject an unsafe cross-lifetime reference before publication and report the module, generation, endpoint, metadata category, and offending artifact.
- **FR-007**: The solution MUST preserve specific schemas and MUST NOT achieve unloadability by replacing module contracts with untyped placeholders, hiding documented operations, or disabling real API documentation generation.
- **FR-008**: The solution MUST NOT depend on private framework cache clearing, reflection into framework internals, sleeps, timed eviction, process restart, or production forced-garbage-collection behavior.
- **FR-009**: Evidence MUST exercise the real API-description and OpenAPI generation paths used by the host in the same lifecycle cycle as endpoint invocation and source-generated serialization.
- **FR-010**: Unload evidence MUST cover at least three consecutive load, document, invoke, replace or remove, dispose, and collection cycles per representative dynamic owner.
- **FR-011**: Contract evidence MUST be immutable, captured before production migration changes, hash-verified, differentially compared, and guarded by exhaustive, bite-proof approval handling.
- **FR-012**: The implementation MUST include a regression guard that detects future host-retained references to collectible artifacts across request metadata, response metadata, route metadata, operation metadata, serialization metadata, transformers, delegates, dependency injection, and disposal.
- **FR-013**: Static host endpoints and dynamic module endpoints MUST coexist in the same document without duplicate or missing operations.
- **FR-014**: If the root cause is confirmed to be an upstream framework defect after an Elsa-independent reproduction, the work unit MUST record or link the upstream report and retain an Elsa-owned safe boundary rather than waiting on an unbounded external fix.
- **FR-015**: The work unit MUST publish a decision report and, if it establishes a new architectural default, an ADR or ratification proposal that defines ownership, lifecycle, allowed metadata, rejection policy, and migration guidance.

### Key Entities

- **Documentation generation**: One immutable, accepted set of endpoint descriptions associated with a module generation and visible atomically to document consumers.
- **Stable documentation artifact**: A schema, value, identifier, or descriptor whose lifetime is owned by the host and which contains no live reference to a collectible module artifact.
- **Collectible artifact**: Any assembly, type, delegate, metadata instance, serializer context, service provider, or object graph owned by a dynamically unloadable module generation.
- **Contract evidence set**: The immutable before HTTP/OpenAPI fixtures, provenance receipt, comparison result, and explicit approved differences for a migrated owner.
- **Boundary diagnostic**: A deterministic rejection record that identifies ownership and the exact unsafe reference path.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Representative dynamic owners complete three consecutive real load/document/invoke/unload cycles with all tracked load-context, assembly, contract-type, delegate, serializer, provider, and endpoint weak references becoming unreachable after bounded collection attempts.
- **SC-002**: Differential comparison reports 100% equality for all unapproved HTTP and OpenAPI facets, and every approved difference is exact, unique, consumed, and mutation-tested.
- **SC-003**: At least one nested request graph and one nested response graph retain their complete documented schemas while their implementation generation remains collectible.
- **SC-004**: Concurrent document requests during successful replacement observe only complete old or complete new generations; failed replacement yields 0 missing-operation windows and preserves the previous generation.
- **SC-005**: Deliberately unsafe metadata in every covered category is rejected before visibility with one deterministic, owner-aware diagnostic.
- **SC-006**: The two blocked REST migration waves can reuse the boundary without weakening their frozen contracts or adding owner-specific cache workarounds.
- **SC-007**: The affected owner suites, architecture guards, full build, generated-map check, formatting check, and relevant backend end-to-end tests all pass with no new warnings attributable to the boundary.

## Assumptions

- HTTP/JSON and OpenAPI remain the selected public management/API protocol; this work does not reopen the protocol decision.
- Dynamic implementation assemblies remain collectible, while explicitly shared contract assemblies may require host lifetime and restart semantics when their identity changes.
- API documentation may be generated on demand and cached, provided cache ownership follows the accepted generation boundary and retired generations are released.
- Existing immutable before fixtures from the REST migration program remain the contract source of truth for affected owners.
- This work defines the first-party Elsa-module boundary. Independently authored third-party plugins that cannot share a stable wire-contract lifetime require a separately approved serialized-document or non-unloadable classification.
- A representative owner must include real typed requests, typed responses, authorization metadata, source-generated serialization, and module replacement; a mapper-only weak-reference test is insufficient.
- Framework §2.24 is draft and unratified. Any structural pattern not already covered by the adapter/bridge, stable contract, or contribution catalog requires explicit architect ratification before broad adoption.

## Out of Scope

- Replacing OpenAPI with another documentation protocol.
- Weakening schemas or omitting dynamic endpoints from documentation as a permanent solution.
- Migrating the blocked Workflows Design or Runtime endpoints inside this prerequisite work unit.
- Modifying private framework caches or relying on undocumented framework implementation details.
- Redesigning established public routes, permissions, or payloads except through separately approved compatibility differences.
- Designing a universal serialized OpenAPI contribution protocol for arbitrary third-party plugins.

# Feature Specification: REST API Migration Compatibility and Authoring Gates

**Feature Branch**: `codex/1346-rest-api-migration-gates`

**Created**: 2026-08-15

**Status**: Complete

**Input**: User description: "Deliver issue #1346: add reusable REST API migration compatibility evidence, deterministic endpoint inventory, authoring and security gates, permission-catalog ownership validation, and collectible-context retention evidence."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prove a module migration preserves its public contract (Priority: P1)

As a module maintainer, I can capture a module's externally visible REST behavior before an endpoint-authoring migration and compare it with the migrated module so that framework replacement cannot silently redesign the public API.

**Why this priority**: The program cannot safely begin its canary migration until maintainers can distinguish an authoring-only change from a public contract change.

**Independent Test**: Capture the representative module before and after an authoring replacement, compare both evidence sets, and verify that unchanged behavior passes while an intentional response or documentation change fails unless it has an explicit approval.

**Acceptance Scenarios**:

1. **Given** a representative module with a recorded baseline, **When** the same public behavior is exposed through a different authoring model, **Then** the compatibility comparison passes without approved differences.
2. **Given** a changed route, method, binding rule, payload, status code, error response, paging/filtering behavior, stream behavior, or consumed API description, **When** no matching approval exists, **Then** the comparison fails and identifies the endpoint and changed facet.
3. **Given** an intentional, reviewed contract change, **When** a precise approval records that difference, **Then** only that difference is accepted and unrelated drift still fails.

---

### User Story 2 - Inspect and gate every enabled first-party endpoint (Priority: P1)

As an architecture steward, I can inspect one deterministic inventory of enabled first-party REST endpoints and enforce ownership, security disposition, permission ownership, and bounded legacy-authoring rules before a change is accepted.

**Why this priority**: Contract parity alone cannot prevent a new endpoint from bypassing authorization or expanding the transitional endpoint framework without a migration plan.

**Independent Test**: Build the inventory for representative hosts and apply valid and intentionally invalid endpoint registrations, proving that complete metadata passes and each missing, ambiguous, or unapproved condition fails with an owning module and endpoint diagnostic.

**Acceptance Scenarios**:

1. **Given** an enabled first-party endpoint, **When** the inventory is produced, **Then** it contains its normalized route, methods, owning module, authoring model, security disposition, content types, and relevant response metadata in stable order.
2. **Given** an endpoint with no security disposition or more than one primary disposition, **When** the authoring gate runs, **Then** the gate rejects it with an actionable endpoint-owner diagnostic.
3. **Given** a new or expanded legacy-framework registration without an approved transition exception, **When** the authoring gate runs, **Then** the gate rejects it.
4. **Given** a permission-protected endpoint, **When** its permission is missing from the active catalog or has zero or multiple owning modules, **Then** the gate rejects it; the reserved administrative wildcard grant is not treated as a catalog-owned endpoint permission.

---

### User Story 3 - Diagnose unloadability regressions (Priority: P2)

As a modular-host maintainer, I can repeatedly load, exercise, and unload an endpoint module and receive evidence that distinguishes route, dependency-injection, and serialization retention so that unloadability claims rely on collectible-context proof rather than process-memory observations.

**Why this priority**: Dynamically unloadable modules are a specialized surface, but retained endpoint assemblies can invalidate the program's chosen authoring model and host lifecycle.

**Independent Test**: Run the harness against a clean collectible module and against deliberately retained route, service, and serializer references, verifying collection in the clean case and a correctly classified failure in each retained case.

**Acceptance Scenarios**:

1. **Given** a collectible endpoint module with all generation-owned references released, **When** repeated load/exercise/unload cycles complete, **Then** weak-reference evidence confirms collection within a bounded verification window.
2. **Given** a deliberately retained route, service, or serializer reference, **When** the same cycle runs, **Then** the harness fails and identifies the retention stage rather than reporting only aggregate memory growth.
3. **Given** several successful cycles, **When** their evidence is compared, **Then** the result is repeatable and includes enough lifecycle context to diagnose a future regression.

### Edge Cases

- Multiple HTTP methods on one route are represented independently enough to detect a partial method change without duplicating shared metadata.
- Semantically equivalent route templates use one normalized comparison shape while retaining owner-readable diagnostics.
- Endpoints with intentionally public, host-credential, or explicitly owned named-policy dispositions are valid without inventing a permission.
- A disabled module contributes no enabled endpoint obligation, while enabling it immediately subjects its endpoints and permissions to all gates.
- Duplicate permission declarations from the same owner are deterministic; declarations from different owners fail rather than becoming order-dependent.
- Approved differences are exact: an approval for one endpoint, method, or contract facet cannot mask drift elsewhere.
- Streaming comparisons do not require unbounded capture and preserve cancellation, media type, framing, and terminal behavior.
- Unload verification does not report success while a strong reference to the collectible context or one of its loaded types remains in the harness itself.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST produce a deterministic manifest for enabled first-party REST endpoints in representative hosts.
- **FR-002**: Each manifest entry MUST identify normalized route templates, HTTP methods, endpoint owner, authoring model, primary security disposition, declared content types, and response metadata relevant to compatibility review.
- **FR-003**: Manifest output MUST be independent of endpoint discovery and registration order and MUST retain enough original identity information for actionable diagnostics.
- **FR-004**: The system MUST capture and compare externally consumed HTTP behavior covering binding, JSON shape, status codes, structured error responses, paging/filtering behavior, and bounded streaming behavior.
- **FR-005**: The system MUST capture and compare the consumed API-description surface for the same representative endpoints.
- **FR-006**: A comparison MUST fail on every unapproved difference and identify the affected endpoint, method, and contract facet.
- **FR-007**: An approved difference MUST be explicit, narrowly scoped, attributable to an owner and reason, and incapable of accepting unrelated drift.
- **FR-008**: The same compatibility evidence MUST support a before-and-after comparison when only the endpoint-authoring model changes.
- **FR-009**: Every enabled first-party endpoint MUST declare exactly one primary security disposition: Foundation permission, intentional public access, host credential, or an explicitly owned named authorization policy.
- **FR-010**: The architecture gate MUST reject missing or ambiguous security dispositions with the endpoint and owner identified.
- **FR-011**: The architecture gate MUST reject new or expanded first-party legacy-framework registrations unless they match a current, explicitly approved transition exception.
- **FR-012**: Transition exceptions MUST be specific to an owned endpoint surface, record a removal owner and follow-up, and MUST NOT permit dynamically unloadable endpoint modules to use the legacy framework.
- **FR-013**: Every permission required by an enabled first-party endpoint MUST exist in the active permission catalog and have exactly one owning module.
- **FR-014**: The reserved administrative wildcard grant MUST be excluded from endpoint-permission catalog ownership requirements and MUST NOT be accepted as an endpoint's declared permission.
- **FR-015**: Permission and endpoint ownership validation MUST allow an endpoint owner to consume a uniquely catalog-owned permission from another module, while failing deterministically for absent catalog owners, conflicting catalog owners, and endpoint owner/disposition mismatches.
- **FR-016**: The system MUST provide repeatable collectible-context lifecycle evidence across multiple load, exercise, unload, collection, and verification cycles.
- **FR-017**: Unload evidence MUST distinguish strong-reference retention attributable to route publication, service composition, serialization, or the verification harness itself.
- **FR-018**: A failed unload cycle MUST retain diagnostic evidence without retaining the collectible module strongly enough to invalidate subsequent verification.
- **FR-019**: The compatibility and authoring gates MUST be reusable by the canary and representative module migrations without copying host setup or comparison logic into each module test suite.
- **FR-020**: This work MUST NOT migrate a production module, alter public route contracts, implement atomic dynamic route publication, or retire the transitional endpoint framework.

### Key Entities

- **Endpoint Manifest**: A stable, ordered description of the enabled first-party REST surface for a representative host.
- **Endpoint Manifest Entry**: One owned route-and-method surface with authoring, security, content, and response compatibility facts.
- **Compatibility Evidence Set**: The HTTP behavior and consumed API-description observations captured for a module version.
- **Approved Difference**: A precise, owned authorization for one intentional compatibility delta, including its reason and scope.
- **Transition Exception**: A bounded record allowing an existing legacy-authoring surface to remain temporarily while naming its removal owner and follow-up.
- **Permission Ownership Record**: The active catalog declaration that maps one endpoint permission to exactly one module owner.
- **Unload Cycle Evidence**: Weak-reference and lifecycle observations for one collectible module cycle, including the stage at which retention was detected.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Ten consecutive manifest captures of an unchanged representative host are byte-for-byte identical.
- **SC-002**: The compatibility suite detects 100% of intentional mutations across route, method, binding, JSON, status, structured error, paging/filtering, streaming, and consumed API-description fixtures.
- **SC-003**: An approved difference accepts only its named endpoint, method, and facet; mutations to any other facet still fail in 100% of negative cases.
- **SC-004**: Every enabled first-party endpoint in the representative hosts has exactly one owner and one valid security disposition, with zero unclassified endpoints.
- **SC-005**: Every enabled permission-protected endpoint resolves to exactly one active catalog owner, excluding the administrative wildcard, with zero missing or conflicting ownership records.
- **SC-006**: Intentional missing-disposition, ambiguous-disposition, unapproved legacy-registration, missing-permission, and conflicting-owner mutations all fail with endpoint-and-owner diagnostics.
- **SC-007**: A clean collectible endpoint module is confirmed collected in every cycle of a repeated run, while deliberate route, service, and serializer retention fixtures are each classified at the correct stage.
- **SC-008**: The canary migration can consume the shared evidence and gates without duplicating the harness implementation or changing its comparison rules.

## Assumptions

- The first-party REST consolidation program and ADR 0068 define the target authoring and security dispositions; this work makes those decisions enforceable rather than reopening them.
- Foundation Identity's merged permission-policy and catalog-ownership contracts are the sole permission semantics source.
- Representative hosts are sufficient for this slice when they collectively activate every endpoint ownership and security shape needed by the upcoming canary and representative migrations.
- Existing public HTTP/JSON behavior is the baseline; intentional public contract redesign requires a separately reviewed approval recorded as a narrow difference.
- Dynamic route collision and atomic generation replacement remain owned by #1345; this slice may inspect dynamic endpoint metadata but does not implement publication lifecycle changes.
- Production migrations remain owned by #1347, #1348, and #1349; this slice delivers only reusable evidence and enforcement infrastructure.

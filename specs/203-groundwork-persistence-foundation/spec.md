# Feature Specification: Groundwork Persistence Foundation

**Feature Branch**: `codex/groundwork-persistence-foundation`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "Define Groundwork as a provider-neutral persistence framework with manifests, planners, portable document storage, and an Elsa validation bridge."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Define Groundwork As A Generic Product (Priority: P1)

An architect can read the Groundwork specification and understand what belongs in the generic persistence framework, what belongs in application-specific integrations, and why Elsa is only the first validation consumer rather than part of the generic product language.

**Why this priority**: Without a clean product boundary, provider and Elsa work will leak application concepts into reusable contracts and make later repository extraction expensive.

**Independent Test**: Review the specification and planning artifacts and verify that Groundwork concepts are named generically while Elsa-specific validation is isolated to the integration boundary.

**Acceptance Scenarios**:

1. **Given** the Groundwork specification, **When** an architect reviews the framework purpose and scope, **Then** they can distinguish Groundwork kernel responsibilities from Elsa integration responsibilities.
2. **Given** a proposed concept such as workflow definition, activity catalog, secret, or bookmark, **When** it is evaluated against the Groundwork boundary, **Then** the concept is classified as an application-side use of generic storage vocabulary rather than a generic Groundwork primitive.
3. **Given** the future extraction goal, **When** the package map is reviewed, **Then** the generic projects can move out of the Elsa Foundation repository without renaming Elsa-specific types.

---

### User Story 2 - Classify Persistence Workloads Before Implementation (Priority: P2)

A persistence designer can classify each planned storage use case by workload family before choosing the portable document store, relational planning, physicalization, or a specialized provider contract.

**Why this priority**: Treating all persistence as the same workload would overextend the portable document store and risk applying it to runtime queues, logs, or checkpoint paths that need different guarantees.

**Independent Test**: Take the roadmap's sample candidates and classify each into a workload family with an allowed default and an explicit benchmark or specialization rule where needed.

**Acceptance Scenarios**:

1. **Given** a metadata/configuration store, **When** it is classified, **Then** the portable document store is an acceptable default unless provider capabilities fail.
2. **Given** a runtime checkpoint state store, **When** it is classified, **Then** the plan marks it as benchmark-gated and not automatically covered by the portable document default.
3. **Given** an operational stream such as execution logs, queue backlog, or outbox records, **When** it is classified, **Then** the plan treats it as specialized unless later evidence proves Groundwork can satisfy the workload.

---

### User Story 3 - Rewrite The Roadmap In Groundwork Terms (Priority: P3)

A delivery planner can take the original Persistence vNext roadmap and execute it as Groundwork-first slices validated by Elsa, while preserving the original staged proof points and provider order.

**Why this priority**: The roadmap already has useful provider and hardening sequencing, but it must be reframed so the generic framework is validated by Elsa rather than embedded inside Elsa.

**Independent Test**: Compare the original roadmap with the Groundwork execution plan and verify that every original slice has a Groundwork-first equivalent, an Elsa validation point, or an explicit deferral.

**Acceptance Scenarios**:

1. **Given** the original S1 core manifest and planner slice, **When** it is rewritten, **Then** the result targets Groundwork core manifests, provider capabilities, relational planning, document planning, and schema history.
2. **Given** the original provider slices, **When** they are rewritten, **Then** provider packages remain Groundwork-owned and Elsa consumes them through an integration bridge.
3. **Given** the original runtime evaluation slice, **When** it is rewritten, **Then** the result produces a go/no-go decision for Elsa runtime stores rather than assuming runtime migration.

### Edge Cases

- If a feature needs application-specific terminology to be understandable, the terminology stays in the application integration layer and maps onto generic Groundwork vocabulary.
- If a workload can fit the document store only after provider-specific physicalization, the default classification must state that requirement instead of presenting the portable store as sufficient.
- If a provider cannot support a declared manifest capability, the planning surface must require a clear startup/materialization failure or an explicit fallback decision.
- If a later extraction would require moving Elsa-facing APIs, the package boundary is considered wrong and must be revised before implementation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The specification MUST define Groundwork as a generic provider-neutral persistence framework, independent of Elsa domain concepts.
- **FR-002**: The specification MUST define the boundary between Groundwork kernel responsibilities and application integration responsibilities.
- **FR-003**: The specification MUST identify a candidate package and namespace structure that can remain inside this repository during validation and later move to a standalone repository.
- **FR-004**: The specification MUST define a persistence workload taxonomy that separates metadata/configuration, catalog/authored data, runtime-defined business data, runtime continuation state, and operational streams.
- **FR-005**: The specification MUST state which workload families are default Groundwork candidates, which are benchmark-gated candidates, and which are specialized-provider candidates by default.
- **FR-006**: The specification MUST identify the minimum manifest vocabulary needed before provider implementation begins.
- **FR-007**: The specification MUST require that manifests express storage intent and workload properties rather than provider-specific database shape.
- **FR-008**: The specification MUST define how the original Persistence vNext roadmap maps to Groundwork-first execution slices.
- **FR-009**: The specification MUST preserve Elsa as the first validation consumer through a clearly named Elsa integration bridge.
- **FR-010**: The specification MUST require provider capability reporting and materialization diagnostics as first-class planning concerns.
- **FR-011**: The specification MUST require schema/materialization history for applied plans.
- **FR-012**: The specification MUST require that unindexed or unsupported portable queries fail clearly rather than silently degrading to scans.
- **FR-013**: The specification MUST require that runtime continuation state and operational streams receive explicit go/no-go evaluation before any generic-store migration.
- **FR-014**: The specification MUST avoid introducing Elsa-specific type names, workflow concepts, or module names into generic Groundwork concepts.
- **FR-015**: The specification MUST define acceptance criteria for the G0 product-definition slice before G1 implementation starts.

### Key Entities *(include if feature involves data)*

- **Groundwork Kernel**: The reusable product layer that owns generic persistence vocabulary, manifest validation, planning, capability reporting, document storage contracts, schema history, and provider diagnostics.
- **Application Integration Layer**: A consumer-specific bridge that discovers application declarations, maps application concepts to Groundwork manifests, registers providers, and exposes application diagnostics.
- **Storage Manifest**: A provider-neutral declaration of storage intent, workload properties, indexes, lifecycle, concurrency expectations, serialization expectations, schema version, and materialization policy.
- **Storage Unit**: A named durable unit inside a manifest, such as a document kind, catalog item, projection, runtime state group, or specialized operational surface.
- **Workload Classification**: The declared family and behavioral profile that determines whether portable document storage, relational planning, physicalization, or specialized providers are valid.
- **Provider Capability Report**: A provider-owned statement of supported manifest features, unsupported features, warnings, fallbacks, and materialization readiness.
- **Materialization Plan**: A provider-specific plan produced from provider-neutral storage intent and used to preview, apply, and record physical storage changes.
- **Elsa Validation Bridge**: The Elsa-side integration that validates Groundwork against real Elsa stores while keeping Groundwork free of Elsa domain names.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reviewer can classify at least ten roadmap storage candidates into Groundwork default, benchmark-gated, or specialized-provider categories without needing extra terminology.
- **SC-002**: The Groundwork package map contains zero Elsa-prefixed generic packages and identifies all Elsa-facing packages separately.
- **SC-003**: Every original Persistence vNext roadmap slice S1-S8 has a corresponding Groundwork execution slice, Elsa validation slice, or explicit deferral note.
- **SC-004**: The G0 artifacts contain no unresolved `[NEEDS CLARIFICATION]` markers.
- **SC-005**: The G0 artifacts define enough manifest vocabulary for G1 planning to start without re-opening the product boundary.
- **SC-006**: The G0 artifacts include at least one explicit rule preventing runtime queues, execution logs, outbox records, and distributed locks from being silently treated as ordinary documents.

## Assumptions

- Groundwork will initially live in the Elsa Foundation repository under non-Elsa project roots so its extraction boundary can be validated before a standalone repository move.
- The initial public product name and root namespace are `Groundwork`, subject to a later naming/trademark check before public package release.
- The first validation consumer is Elsa, but the generic framework must not reference Elsa packages or Elsa domain concepts.
- The existing Persistence vNext roadmap remains useful as sequencing evidence, but its slices will be renamed and scoped around Groundwork.
- Code implementation does not begin in G0; G0 produces product-definition and execution-planning artifacts that unblock G1.
- Runtime continuation state and operational streams remain benchmark-gated or specialized until evidence proves Groundwork can satisfy their required semantics.

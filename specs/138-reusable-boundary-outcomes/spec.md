# Feature Specification: Reusable Activity Boundary Outcomes

**Feature Branch**: `codex/1012-reusable-boundary-outcomes`

**Created**: 2026-07-23

**Status**: Draft

**Input**: User description: "Deliver GitHub issue #1012 end to end, including any work on elsa-foundation-studio if necessary."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Branch on reusable activity results (Priority: P1)

As a workflow author, I can declare multiple named outcomes on a reusable activity boundary and map each one to a reachable outcome produced by the reusable graph, so a parent workflow can continue along the matching branch.

**Why this priority**: This is the requested capability and removes the current single-`done` limitation at the reusable activity boundary.

**Independent Test**: Publish a reusable graph whose entry activity can complete with `approved` or `rejected`, map both results to boundary outcomes, execute it from a parent flowchart, and verify that only the connection matching the actual result runs.

**Acceptance Scenarios**:

1. **Given** a reusable graph with two reachable entry outcomes and two explicit boundary mappings, **when** publication completes, **then** the reusable activity contract exposes both named outcomes.
2. **Given** a parent flowchart with separate connections for the two reusable activity outcomes, **when** the reusable graph completes with one mapped result, **then** only the parent connection for that result runs.
3. **Given** a reusable graph with a declared emitted boundary outcome that has no valid reachable mapping, **when** publication is attempted, **then** publication is rejected with a diagnostic identifying the invalid mapping.

---

### User Story 2 - Preserve existing reusable activities (Priority: P2)

As an operator, I can continue to publish and execute existing schema-1 reusable activities without editing them, and they still complete with `done`.

**Why this priority**: Existing persisted manifests and compiled artifacts must remain compatible.

**Independent Test**: Publish and execute an unchanged schema-1 reusable graph and verify that its public contract and runtime completion remain `done`.

**Acceptance Scenarios**:

1. **Given** an existing schema-1 reusable graph with the implicit `done` boundary, **when** it is proposed, validated, compiled, and executed, **then** its observable behavior is unchanged.
2. **Given** a schema-1 reusable graph that declares another emitted outcome, **when** publication is attempted, **then** the existing schema-1 validation rule still rejects it.

---

### User Story 3 - Author and connect every boundary outcome (Priority: P3)

As a workflow author using a catalog-driven designer, I can see and connect every emitted outcome declared by a reusable activity without designer-specific knowledge of the reusable graph schema.

**Why this priority**: The runtime capability is only usable end to end when catalog consumers can render the reusable activity's outcome ports.

**Independent Test**: Publish a reusable activity with multiple emitted outcomes, load its authoring catalog entry, and verify that every emitted outcome appears exactly once as an outcome port that can be connected and round-tripped.

**Acceptance Scenarios**:

1. **Given** a published reusable activity with `approved` and `rejected` emitted outcomes, **when** its authoring catalog entry is loaded, **then** it exposes outcome ports named `approved` and `rejected`.
2. **Given** a designer that already renders catalog outcome ports generically, **when** the reusable activity is placed in a flowchart, **then** no reusable-activity-specific rendering change is required.

### Edge Cases

- A boundary outcome is declared but not mapped.
- A boundary outcome is mapped more than once.
- A mapping references an unknown boundary outcome or an outcome not emitted by the direct entry activity contract.
- Two mappings could match the same child completion outcome.
- The graph entry completes with no outcome, multiple outcomes, or an unmapped outcome.
- Outcome display names differ from their stable references or runtime names.
- The reusable graph has no direct entry activity or its dependency cannot be resolved.
- An older compiled schema-1 artifact is executed after schema 2 is introduced.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The graph activity provider MUST support a new manifest schema that lets the provider own explicit mappings from emitted reusable-boundary outcomes to emitted outcomes of the graph's resolved direct entry activity.
- **FR-002**: Each mapping MUST identify both sides by stable contract outcome reference during design and compilation.
- **FR-003**: Every boundary outcome declared as emitted MUST have exactly one mapping, and mappings MUST NOT target boundary outcomes that are absent or not emitted.
- **FR-004**: Every mapping source MUST resolve to an outcome declared as emitted by the resolved direct entry activity dependency.
- **FR-005**: Validation MUST reject duplicate, unknown, missing, unreachable, or ambiguous mappings with deterministic provider diagnostics.
- **FR-006**: Compilation MUST pin the validated source and boundary runtime outcome names into the provider-owned runtime descriptor; execution MUST NOT consult mutable design state.
- **FR-007**: When the direct entry activity completes with exactly one mapped outcome, the reusable boundary MUST complete with exactly the mapped public outcome.
- **FR-008**: Execution MUST fail deterministically when the direct entry completion has no uniquely mapped outcome.
- **FR-009**: The outcome chosen during child completion MUST be the same outcome committed at the reusable boundary's completion checkpoint.
- **FR-010**: A parent flowchart or reusable activity MUST receive the mapped boundary outcome and schedule only the connection matching that outcome.
- **FR-011**: Published activity catalog metadata MUST expose every emitted reusable-boundary outcome exactly once as a generic outcome port.
- **FR-012**: Schema-1 manifests, proposals, validation, compilation, persisted artifacts, and runtime behavior MUST remain unchanged and continue to use the single `done` outcome.
- **FR-013**: Schema-2 contract proposal MUST preserve author-authored emitted outcomes and MUST NOT force or replace them with `done`.
- **FR-014**: A newly emitted boundary outcome MUST be treated as a semantic contract expansion according to the existing activity-contract version policy.
- **FR-015**: Compilation and publication MUST remain atomic: an invalid mapping MUST produce no partially published reusable activity version or catalog entry.
- **FR-016**: Catalog-driven designers MUST be able to render and persist reusable boundary outcome connections without reusable-graph-specific port rendering logic.

### Key Entities

- **Boundary outcome**: A named result emitted by the reusable activity to its parent workflow, with a stable design-time reference and runtime name.
- **Entry outcome**: A named emitted result declared by the resolved direct entry activity contract.
- **Boundary outcome mapping**: The provider-owned association from one entry outcome reference to one emitted boundary outcome reference.
- **Runtime outcome mapping**: The compiled, artifact-contained association between the resolved entry outcome name and public boundary outcome name.
- **Authoring outcome port**: The generic catalog projection of an emitted boundary outcome used by visual workflow designers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reusable activity with at least two mapped outcomes publishes successfully and exposes all declared emitted outcomes in its runtime contract and authoring catalog.
- **SC-002**: For each mapped outcome in an automated parent-flowchart test, exactly one matching branch executes and all nonmatching branches remain unexecuted.
- **SC-003**: Automated validation tests reject every missing, duplicate, unknown, unreachable, and ambiguous mapping case before publication.
- **SC-004**: All existing schema-1 reusable activity tests pass unchanged, including the default `done` proposal and execution behavior.
- **SC-005**: A catalog-driven Studio build renders the published outcome ports using its existing generic port support, or receives the smallest necessary authoring change if mapping creation cannot otherwise be completed.

## Assumptions

- Multiple outcomes are alternative completion results across executions; a single execution selects one boundary outcome.
- Schema 2 is additive and explicit rather than relaxing schema 1, preserving persisted-manifest compatibility.
- Outcome mappings are owned by the graph activity provider because they describe the provider's internal boundary semantics.
- The direct graph entry activity is the completion source for this feature; explicit return nodes and arbitrary descendant completion remain outside this work unit.
- Existing generic activity-contract and flowchart outcome mechanisms are reused.
- The current constitutions are draft/provisional; this work follows their gates without changing or ratifying them.

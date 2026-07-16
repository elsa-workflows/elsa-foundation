# Feature Specification: Deterministic and Bounded Workflow Dispatch

**Feature Branch**: `codex/dispatch-677`

**Created**: 2026-07-16

**Status**: Draft

**Input**: GitHub issue #677, “Keep published dispatch targets deterministic and bounded,” under parent #674

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Publish a validated deterministic dispatch (Priority: P1)

As a workflow author, I can publish a workflow containing `DispatchWorkflow` only when each selected child is visible in the publication request's authorized tenant scope, still has an unambiguous live Published artifact, and the supplied child inputs agree with that artifact's declared workflow inputs.

**Why this priority**: A published parent must capture one valid child behavior rather than a stale authoring choice or an input map that will fail only after deployment.

**Independent Test**: Select a child, alter its availability, publication, or declared inputs before publishing the parent, and verify publication either pins the exact currently valid child dependency or fails with a specific diagnostic.

**Acceptance Scenarios**:

1. **Given** an accessible child definition with one live Published artifact and valid declared inputs, **When** the parent is published, **Then** the parent pins that exact child artifact and a canonical dependency description.
2. **Given** a child selection that became inaccessible, unpublished, ambiguous, or stale after authoring, **When** the parent is published, **Then** publication fails without producing or activating a parent artifact.
3. **Given** statically known child input names or values that do not match the pinned child's declarations, **When** the parent is published, **Then** publication fails with diagnostics identifying each invalid binding.
4. **Given** a dynamic input map whose final values are unavailable at publication, **When** dispatch executes, **Then** the realized map is validated against the pinned child's declarations before dispatch responsibility is committed.
5. **Given** a declared workflow input whose ordinary name resembles runtime context such as `tenant` or `authority`, **When** it is dispatched, **Then** the value remains exclusively in the workflow-input channel and cannot change typed runtime context; undeclared names remain invalid.

---

### User Story 2 - Keep pinned child behavior executable (Priority: P1)

As an operator, I can replace or unpublish a child publication without changing the behavior of already-published parents, because each retained parent keeps its exact pinned child dependency available for as long as the parent can still execute.

**Why this priority**: Content-addressed publication is only deterministic when a retained parent cannot lose or silently retarget one of its executable dependencies.

**Independent Test**: Publish a parent and child, replace or unpublish the child, then start the retained parent and verify it still dispatches the original pinned child; retire the last parent retention root and verify the dependency becomes eligible for collection.

**Acceptance Scenarios**:

1. **Given** a parent pins a child artifact and that child's transitive dependencies, **When** the child is republished with new behavior, **Then** the existing parent continues to use the original dependency closure.
2. **Given** a pinned child source is unpublished after the parent is published, **When** the retained parent executes, **Then** the original pinned child can still start by retained artifact identity and immutable provenance.
3. **Given** multiple retained parents share a dependency, **When** one parent is retired, **Then** the dependency remains retained until no live source, retained execution, retained parent artifact, or other retention root reaches it.
4. **Given** the last retention root for an artifact dependency closure is removed, **When** collection runs, **Then** artifacts no longer reachable from any root become eligible for collection.
5. **Given** a parent publication is retired, **When** a new root start is requested through that retired publication, **Then** the start is rejected without altering the immutable parent artifact.
6. **Given** an explicit runtime start-deny policy matches a parent or child artifact, **When** a future start is requested, **Then** the start is rejected without deleting or rewriting the artifact and without affecting already-materialized execution state.

---

### User Story 3 - Bound recursive dispatch safely (Priority: P1)

As an operator, I am protected from direct or indirect runaway workflow dispatch while still being able to intentionally call an older version of the same workflow definition when the artifact graph is safe.

**Why this priority**: Dispatch creates a cross-execution call graph. Exact cycles must never be publishable, and version-skewed graphs that are not exact cycles still need a runtime bound.

**Independent Test**: Attempt direct and transitive exact-artifact cycles at publication, then execute version-skewed and indirect chains at, below, and above the configured nesting limit.

**Acceptance Scenarios**:

1. **Given** a candidate parent artifact would depend directly or transitively on its own exact artifact identity, **When** publication validates the dependency closure, **Then** publication is rejected with the cycle path.
2. **Given** a newer artifact dispatches an older artifact of the same definition without an exact-artifact cycle, **When** publication validates it, **Then** publication succeeds.
3. **Given** a dispatch chain whose next child would remain within the configured maximum nesting depth, **When** the child start is requested, **Then** dispatch proceeds with the incremented depth recorded as runtime lineage.
4. **Given** a dispatch chain whose next child would exceed the configured maximum nesting depth, **When** the child start is requested, **Then** the start fails deterministically before child materialization and exposes a safe diagnostic.
5. **Given** no host override, **When** nesting depth is evaluated, **Then** the maximum allowed dispatch depth is 32.

### Edge Cases

- A selected definition resolves to multiple simultaneously live Published source references.
- A child publication changes between authoring, parent dependency resolution, and parent activation; the resolution-time exact pin remains authoritative and activation never retargets it.
- A statically known input is renamed, removed, made required, or changes declared type in the selected child artifact.
- A dynamic input map contains duplicate, blank, unknown, missing-required, or incompatible values, including names that resemble runtime fields but are not declared child inputs.
- A parent has multiple dispatch nodes targeting the same child, or several parents share one transitive dependency.
- A dependency closure is a diamond graph and must be canonicalized without double-counting shared artifacts.
- A dependency artifact or dependency edge is missing or inconsistent while publication or start validation runs.
- Replacement and collection overlap; collection must not remove an artifact that publication or execution has established as a root.
- A chain includes several versions of one definition but no repeated artifact identity.
- The configured maximum depth is invalid, reduced below an in-flight chain, or reached exactly.
- A runtime deny decision is introduced or removed after an executable is published.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Parent publication MUST revalidate that every selected child definition is visible in the publication request's tenant access scope and resolves to exactly one current live Published artifact.
- **FR-002**: Publication MUST fail closed for stale, inaccessible, missing, ambiguous, or internally inconsistent child selections and MUST NOT activate a partial parent publication.
- **FR-003**: Each published dispatch node MUST retain the exact child artifact identity plus immutable parent-artifact/node dependency provenance approved by publication; retained execution MUST NOT depend on historical child publication provenance.
- **FR-004**: Statically knowable child input names and values MUST be validated against a versioned declared workflow-input contract on the pinned child artifact during publication; a legacy child lacking that contract or a trustworthy dependency graph MUST be recompiled and republished before it can become a new strict dispatch target.
- **FR-005**: Realized dynamic input maps MUST be validated against the same pinned declared-input contract before dispatch responsibility is committed.
- **FR-006**: Input validation MUST reject blank or unknown names, duplicate logical names, missing required inputs, and incompatible values; every accepted declared name MUST route only to workflow inputs even when its text resembles a runtime-context field.
- **FR-007**: Dispatch inputs MUST remain isolated to the child workflow-input channel and MUST NOT mutate child variables, stimulus, execution identity, lineage, correlation inheritance rules, tenant, partition, authority, root initiator, or run kind.
- **FR-008**: Validation failures MUST produce safe, actionable diagnostics without retaining rejected raw input values in dispatch operational state.
- **FR-009**: Every executable artifact MUST expose a canonical direct dependency set for the exact child artifacts pinned by its dispatch nodes.
- **FR-010**: The parent artifact's behavioral identity MUST include every canonical direct dependency identity and full behavioral hash, so transitive behavior is included inductively through child hashes.
- **FR-011**: Equivalent dependency closures MUST produce the same behavioral identity regardless of traversal order or shared diamond paths; a behavioral change anywhere in the closure MUST change the dependent parent's identity.
- **FR-012**: Executable retention MUST treat pinned dependency edges as transitive reachability from live source references, retained execution roots, retained parent artifacts, and any other existing artifact roots.
- **FR-013**: Dependency retention and collection MUST be race-safe with publication activation, execution-root creation, and concurrent collection.
- **FR-014**: A retained published parent MUST remain able to start its exact pinned child after that child's source reference is replaced, retired, or unpublished.
- **FR-015**: Starting a retained pinned child MUST validate the full child artifact ID/hash against immutable parent-artifact/node dependency provenance without requiring historical child source provenance to remain live.
- **FR-016**: Retiring a parent publication MUST prevent new starts through that publication while leaving the immutable artifact and other retention roots unchanged.
- **FR-017**: Runtime composition MUST support an explicit start-deny policy that can reject future starts by immutable artifact context without rewriting or deleting the executable.
- **FR-018**: A denied start MUST fail before new workflow execution state is materialized and MUST expose a machine-classifiable safe reason.
- **FR-019**: Publication MUST fail closed when a loaded dependency graph contains a repeated full artifact ID/hash identity, including malformed direct/transitive stored cycles; it MUST also reject the candidate if its computed full identity appears in the closure.
- **FR-020**: Exact-artifact cycle diagnostics MUST identify a deterministic dependency path using full artifact ID/hash identity without relying on workflow definition identity.
- **FR-021**: Different artifact versions of the same definition MUST remain legal dependencies when they do not create an exact-artifact cycle.
- **FR-022**: Runtime start lineage MUST carry a dispatch nesting depth that is incremented only for cross-workflow dispatch starts and inherited through deferred/global delivery.
- **FR-023**: Runtime MUST reject a dispatch start that would exceed the configured maximum nesting depth before child materialization.
- **FR-024**: The default maximum dispatch nesting depth MUST be 32 and hosts MUST be able to configure a positive finite alternative.
- **FR-025**: Redelivery and replay at one nesting level MUST preserve the same depth and MUST NOT increment it more than once.
- **FR-026**: Root workflow starts and legacy start payloads without dispatch lineage MUST begin at depth zero.
- **FR-027**: Publication and execution tests MUST cover stale and tenant-inaccessible selections, declared-input failures and channel isolation, dependency hash determinism, replacement/unpublication, shared and transitive retention, malformed exact cycles, version-skewed calls, boundary depth, and over-depth failure.
- **FR-028**: This slice MUST NOT add workflow-definition activity behavior, Studio implementation, broker transport selection, waited completion, lifecycle observation, cancellation propagation, redrive, test-scope dispatch, or distributed dispatch placement.

### Key Entities

- **Executable dependency**: A directed immutable edge from one executable artifact to an exact child artifact, including the child behavioral identity required for deterministic hashing and retention.
- **Dependency closure**: The canonical, de-duplicated set of all artifacts transitively reachable from a parent executable's direct dependencies.
- **Declared child input contract**: The immutable set of workflow input names, requirements, and value expectations associated with the pinned child artifact.
- **Dispatch nesting lineage**: The runtime-owned depth carried from a parent dispatch to its child start; root starts begin at zero.
- **Runtime start-deny decision**: A policy result that prevents a future artifact start without mutating the artifact or existing execution state.
- **Retention root**: A live reference or retained runtime record from which executable dependencies remain reachable and therefore unavailable for collection.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All stale, inaccessible, unpublished, ambiguous, and invalid-input publication cases are rejected before parent activation, with zero partial parent artifacts activated.
- **SC-002**: For every successful parent publication, inspection shows one canonical direct dependency per distinct pinned child artifact; recompiling against any changed reachable child behavior produces a different parent identity while an already-retained parent remains unchanged.
- **SC-003**: Reordering dispatch nodes or traversing a shared diamond dependency graph produces exactly one stable dependency closure and identical behavioral identity for equivalent behavior.
- **SC-004**: A parent published before child replacement or unpublication starts the original pinned child successfully in 100% of replacement/unpublication integration scenarios.
- **SC-005**: Collection retains every artifact reachable from at least one root and makes every unreachable artifact in the tested closure eligible after the final root is removed.
- **SC-006**: Direct and transitive exact-artifact cycles are rejected in 100% of cycle tests, while legal newer-to-older version-skewed calls publish successfully.
- **SC-007**: Dispatch chains at depths 1 through 32 succeed under defaults, the next child at depth 33 is rejected before materialization, and custom positive limits exhibit the same boundary behavior.
- **SC-008**: Replay of the same dispatch produces one child at one stable nesting depth, with no depth inflation across duplicate checkpoint, outbox, or start delivery.
- **SC-009**: Runtime deny and retired-publication tests create zero new workflow execution records and do not alter the denied executable's behavioral identity.
- **SC-010**: Architecture and dependency audits report no Studio, broker, distributed-placement, waited-lifecycle, or construct-only workflow-definition activity expansion.

## Assumptions

- Existing content-addressed executable storage, source-reference lifecycle, retained-execution roots, and collection leases remain the authoritative artifact-lifetime mechanisms and are extended rather than replaced.
- Newly compiled child artifacts expose an immutable declared workflow-input contract and trustworthy dependency graph. Legacy artifacts remain readable/executable through compatibility paths but are ineligible as new DispatchWorkflow targets until recompiled and republished.
- An input map whose keys or values cannot be known at publication is allowed only because the realized map is revalidated against the exact pinned child before staging the dispatch checkpoint.
- Retiring or unpublishing a source reference blocks future starts through that reference but does not revoke already-retained executable dependencies.
- Runtime deny policy applies to future start materialization; it does not retroactively terminate an already-materialized execution. Cancellation and revocation behavior remain outside #677.
- Dispatch depth counts cross-workflow dispatch edges: root execution depth is 0, its dispatched child is 1, and the default maximum permitted child depth is 32.
- Exact recursion is based on the full artifact ID/hash pair rather than definition identity. Normal content-addressed publication creates a DAG; the publication check primarily rejects malformed stored graphs and defensively covers candidate identity recurrence.
- Resolution-time child validity is authoritative. Later child replacement/unpublication does not retarget or invalidate the selected exact pin; closure leasing at parent activation protects the selected artifact from collection.
- The current publication API expresses accessibility through tenant scope. Finer-grained author/role ACLs require a future publication authorization surface and are not invented inside this activity.
- Supported literal defaults are materialized into the normalized child input bag before checkpoint staging; unknown type aliases or unsupported default expressions fail publication.
- Groundwork-backed dependency persistence and restart behavior needed by this slice may extend existing artifact/reference persistence, but workflow-dispatch lifecycle record durability and inspection remain owned by #678.
- The broader constitution remains draft/provisional; accepted artifact, publication, retention, and runtime checkpoint decisions plus current repository contracts govern this work.

## Scope Boundaries

### Included

- Publication revalidation and declared child-input validation.
- Canonical executable dependency metadata, behavioral hashing, and transitive retention.
- Retained-pin execution after child replacement or unpublication.
- Parent publication retirement and an explicit future-start deny policy.
- Exact-artifact cycle rejection and configurable runtime dispatch depth with default 32.
- In-memory and applicable durable-provider coverage for artifact dependencies and retention.

### Excluded

- WorkflowDefinitionActivity or any other construct-only workflow-definition activity.
- Studio-specific editors or operational UI; Studio support remains a separate thread.
- Wait-for-completion/resume (#679), terminal child lifecycle and cancellation propagation (#680), exhaustion/redrive (#681), test-scope dispatch (#682), and distributed placement/transport (#683).
- Activity-level broker/transport selection or a MassTransit dependency.
- Retroactive cancellation or revocation of already-materialized child executions.

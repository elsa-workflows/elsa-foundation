# Feature Specification: Reusable Activity Definitions

**Feature Branch**: `598-reusable-activity-definitions`

**Created**: 2026-07-15

**Status**: Draft

**Input**: [Backend PRD #671](https://github.com/elsa-workflows/elsa-foundation/issues/671): "First-class reusable activity definitions and graph-backed execution"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author and publish a graph-backed activity (Priority: P1)

A workflow author creates a reusable activity definition, declares its public inputs, outputs, and outcomes, implements its behavior as an activity graph, and publishes an immutable version that appears in the Activity Catalog.

**Why this priority**: This establishes reusable behavior as an activity concept instead of overloading workflow identity or creating nested workflow executions.

**Independent Test**: Create a definition with one input, one mapped output, and a graph implementation; publish it and verify that the resulting immutable activity version can be selected by exact version identity.

**Acceptance Scenarios**:

1. **Given** a valid activity-definition draft with a graph implementation, **When** it is published, **Then** one immutable activity version and its closed executable template become visible atomically.
2. **Given** a graph-backed activity version, **When** the Activity Catalog is queried, **Then** its provider-neutral public contract is available without exposing provider-specific source as the contract.
3. **Given** a draft with a provider contract that does not match its declared public contract, **When** publication is attempted, **Then** publication fails with structured diagnostics and creates no partial version, dependency, or executable state.

---

### User Story 2 - Execute reusable behavior inside one workflow run (Priority: P1)

A workflow author places an exact activity-definition version in a workflow. When the workflow runs, the reusable graph behaves as an ordinary composite activity: it has a visible outer activity boundary, schedules ordinary descendant activity executions, and never creates another workflow instance.

**Why this priority**: Inline composite execution is the defining behavioral correction to the existing workflow-as-activity concept.

**Independent Test**: Publish an activity whose graph suspends, publish a consuming workflow, run it, tear the host down, restart, resume the descendant bookmark, and verify one workflow execution completes with the expected output.

**Acceptance Scenarios**:

1. **Given** a workflow containing a pinned graph-backed activity version, **When** the workflow runs, **Then** the outer activity and all descendants share one workflow execution identity.
2. **Given** a descendant that creates a bookmark, **When** the host restarts and the bookmark is resumed, **Then** the exact descendant continues without replaying already committed graph work.
3. **Given** successful graph completion, **When** the boundary output is captured, **Then** the output, natural `Done` outcome, outer completion, and parent continuation become durable atomically.
4. **Given** a graph activity is cancelled while a descendant resume races with cancellation, **When** one transition commits first, **Then** the losing transition cannot also complete the scope.

---

### User Story 3 - Pin and upgrade versions explicitly (Priority: P1)

A workflow author can see how two activity versions differ, publish only with an adequate semantic-version increment, and create an explicit bottom-up upgrade plan for affected activity and workflow drafts without mutating anything already published.

**Why this priority**: Exact pinning is only usable if authors can understand compatibility and safely move consumers forward.

**Independent Test**: Publish two versions with a breaking contract change, verify that an insufficient version increment is rejected, inspect the version diff, and apply an upgrade plan to selected drafts while older published consumers remain unchanged.

**Acceptance Scenarios**:

1. **Given** a published consuming workflow, **When** a newer activity version appears, **Then** the workflow remains pinned to its original version.
2. **Given** a breaking public-contract change, **When** the author requests a minor or patch version, **Then** publication is rejected with the required minimum increment and change-level diagnostics.
3. **Given** nested reusable activities with newer versions available, **When** an upgrade plan is produced, **Then** dependencies are ordered bottom-up and every proposed edit is pinned to the observed draft revision and definition head.
4. **Given** a plan whose observed revision or head is stale, **When** it is applied, **Then** the operation fails without partially updating the selected closure.

---

### User Story 4 - Inspect the complete execution hierarchy safely (Priority: P2)

An operator inspecting a workflow run can expand a reusable activity boundary, page through its descendant executions, see the pinned historical layout and causal evidence, and inspect permitted values without loading an unbounded graph or current design state.

**Why this priority**: Reuse must not turn part of a run into an opaque box, especially across suspension, retries, loops, and faults.

**Independent Test**: Run a nested graph with a loop, bookmark, and retry; inspect the outer execution, page through descendants, and verify layout, attempt lineage, aggregate status, authorization, and redaction behavior.

**Acceptance Scenarios**:

1. **Given** an outer graph-activity execution, **When** it is expanded, **Then** its directly related execution hierarchy is returned with stable identities and a continuation cursor when more evidence exists.
2. **Given** a published layout later changed in authoring, **When** an old run is inspected, **Then** the layout pinned with the executed artifact reference is used.
3. **Given** permission to inspect structure but not sensitive values, **When** the hierarchy is read, **Then** structure remains visible while protected values are omitted or redacted explicitly.
4. **Given** descendant failures, retries, or cancellations, **When** the outer boundary is inspected, **Then** its own lifecycle and a separately derived subtree aggregate are both available.

---

### User Story 5 - Add implementation providers without changing Runtime (Priority: P2)

A provider author can contribute a stable provider key, versioned manifest schema, validation, deterministic compilation, and a matching Runtime consumer without changing the Activity Catalog, publishing workflow, or universal Runtime dispatch code.

**Why this priority**: The visual graph is the first implementation shape, but the model must accommodate future JavaScript, C#, CLR, remote, and other providers without another conceptual reset.

**Independent Test**: Model a second provider against the documented contracts and verify that it can propose a public contract, validate a draft, compile a deterministic template, and declare Runtime requirements through provider-owned extensions only.

**Acceptance Scenarios**:

1. **Given** a provider-owned manifest, **When** it is stored and later read, **Then** durable identification uses a stable namespaced provider key and schema version rather than a CLR type name.
2. **Given** provider-inferred contract changes, **When** authoring reconciliation occurs, **Then** they are presented as proposals and cannot silently replace the authoritative draft contract.
3. **Given** a published artifact whose required Runtime consumer is unavailable, **When** activation is attempted, **Then** an artifact-activation incident identifies the missing consumer and ordinary activity retry is not used as deployment recovery.
4. **Given** active retained artifacts, **When** deployment preflight runs, **Then** all required Runtime consumers and supported schemas can be checked before execution or resumption.

---

### User Story 6 - Work safely with drafts and lifecycle policy (Priority: P2)

Authors can maintain multiple drafts, clone from immutable versions, change providers on Design-owned lineages, test drafts through the real Runtime pipeline, and distinguish retirement from revocation.

**Why this priority**: Reusable definitions need a coherent authoring lifecycle that does not reintroduce mutable references or source-authority conflicts.

**Independent Test**: Create two drafts from one version, publish one under an expected head, reject a stale publish from the other without losing it, test-run the remaining draft, and verify retirement affects new selection but not closed published templates.

**Acceptance Scenarios**:

1. **Given** multiple drafts for one definition, **When** one draft publishes, **Then** other drafts remain intact with their immutable lineage and optimistic revision facts.
2. **Given** a CLR-owned definition, **When** an authoring API attempts to replace its content, **Then** the request is rejected because the source remains its sole content authority.
3. **Given** a source-owned definition version, **When** an authorized tenant forks it for customization, **Then** the system creates a new Design-owned definition identity and draft without changing the source lineage.
4. **Given** a Design-owned draft, **When** its provider changes, **Then** the new draft is validated and published as a new immutable version while old manifests remain executable and inspectable.
5. **Given** an activity draft test run, **When** it executes, **Then** it uses an expiring source reference and a synthetic wrapper workflow in the same artifact store and Runtime pipeline as published execution.
6. **Given** a retired dependency already closed into a published parent template, **When** that parent runs, **Then** retirement alone does not invalidate the executable; revocation remains a distinct stronger decision.
7. **Given** the same activity draft revision and Test Run idempotency key, **When** dispatch is repeated or its first acknowledgement is lost, **Then** one durable receipt and one Runtime execution are reused; a new key creates new evidence.
8. **Given** a retained Test Run receipt, **When** status is read by Test Run identity or idempotency key, **Then** dispatch, execution, safe failure, source-reference expiry, Runtime Evidence retention, still-running, and eventual outer activity execution facts are returned without synthetic wrapper/provider payloads.
9. **Given** an advertised cancellation capability, **When** policy allows cancellation of a non-terminal Test Run, **Then** an idempotent Runtime cancellation is requested and status reconciles through requested or cancelling to terminal; otherwise status reports unavailable.

---

### User Story 7 - Migrate Elsa 3 reusable workflows deliberately (Priority: P3)

An Elsa 3 administrator can analyze a collection, review deterministic conversion diagnostics, and atomically convert selected reusable workflows into activity definitions plus wrapper workflows while preserving exact references and direct-start behavior.

**Why this priority**: Foundation takes a clean break from the Elsa 4 pre-release workflow-as-activity surface, but Elsa 3 users still need a trustworthy forward path.

**Independent Test**: Analyze a fixture collection containing direct starts, reusable references, missing references, and a cycle; apply a valid selected closure twice and verify deterministic identities, exact rewrites, wrapper behavior, and idempotence while the cycle remains unapplied with a complete diagnostic path.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 reusable workflow, **When** conversion is planned, **Then** the plan contains both an activity definition and a wrapper workflow preserving the original direct-start identity and public contract.
2. **Given** missing or recursive references, **When** the collection is analyzed, **Then** complete dependency-path diagnostics are reported before any write occurs.
3. **Given** an approved dependency closure, **When** conversion is applied, **Then** exact references are rewritten atomically or no part of the closure is persisted.

### Edge Cases

- Two definitions contain the same behavioral template but have different provenance or layout.
- The same activity version is placed repeatedly or nested at great depth in one workflow.
- A nested dependency graph contains a cycle only at exact-version level, or uses different versions of the same logical definition without a cycle.
- An input is absent, explicitly null, present with a value, supplied by an expression default, or fails durable capture.
- A public value is valid for in-memory use but cannot be captured by the configured durable storage driver.
- A required output is never assigned, or an output mapping fails after descendants have completed.
- Internal work faults before entry commit, after entry commit, during suspension, or during boundary completion.
- Cancellation races with bookmark resumption, timer delivery, or retry dispatch.
- A retry starts after a partial failed attempt and external side effects may already have occurred.
- A provider schema is obsolete but still required by a retained executable.
- A definition, source version, or layout is retired or deleted after a consuming workflow was published.
- Reverse dependency projections are stale or being rebuilt while authoritative direct edges remain available.
- Tenant-authored content attempts to reference another tenant's exact version identifier.
- An inspection page changes while new executions commit, or a cursor belongs to another workflow, boundary, tenant, or authorization context.
- A deeply nested graph exceeds the capacity of a particular host even though Foundation defines no universal size ceiling.

## Requirements *(mandatory)*

### Functional Requirements

#### Definition and authoring lifecycle

- **FR-001**: The system MUST use `ActivityDefinition` as the stable catalog identity for reusable activities regardless of implementation provider.
- **FR-002**: The system MUST support multiple mutable drafts per activity definition, each with an optimistic revision and an immutable optional source-version lineage.
- **FR-003**: The system MUST publish immutable semantic versions with distinct definition identity, version identity, semantic version, and mutable display metadata.
- **FR-004**: The system MUST enforce exactly one content authority for a definition lineage; source-owned lineages MUST reject competing authoring mutations.
- **FR-005**: A Design-owned lineage MUST be able to change implementation provider only by publishing a new immutable version.
- **FR-006**: Workflow drafts and versions MUST reference only immutable activity-definition version identities, never mutable drafts or a "latest" selector.
- **FR-007**: The Activity Catalog MUST remain the source of truth for reusable activity visibility and public contract discovery.

#### Public contract and provider model

- **FR-008**: Every draft and version MUST expose a provider-neutral public contract containing stable reference keys, inputs, outputs, outcomes, type references, requiredness, explicit per-member nullability, defaults, durability requirements, and presentation metadata. Requiredness and nullability are independent facts.
- **FR-009**: The system MUST preserve absent, explicitly null, and present input states as distinct states.
- **FR-010**: Literal and expression-based defaults MUST be supported as caller-side binding templates and MUST be captured into each consuming published artifact.
- **FR-011**: Public boundary values MUST be durable by default and MUST identify durable storage behavior by a stable storage-driver key rather than a CLR type name.
- **FR-012**: Provider implementation manifests MUST be opaque outside the owning provider and identified by a stable namespaced provider key plus schema version.
- **FR-013**: Runtime implementation descriptors MUST use stable consumer keys and schemas independently of Design provider manifest types.
- **FR-014**: Provider-inferred contract changes MUST be proposals; the authoritative draft contract MUST NOT change without an explicit authoring mutation.
- **FR-015**: Publication MUST verify that the provider implementation satisfies the authoritative public contract.
- **FR-016**: Providers MAY require stricter compatibility rules but MUST NOT weaken the platform compatibility baseline.

#### Publication, versions, and dependencies

- **FR-017**: Publication MUST produce an immutable content-addressed executable template whose identity is determined only by canonical behavioral execution material.
- **FR-018**: A published template MUST include exact direct dependency identities, a transitively closed executable dependency set, and provider/compiler fingerprints sufficient to preserve behavior.
- **FR-019**: Validation, dependency resolution, cycle detection, provider compilation, template and source-reference persistence, version creation, and definition-head advancement MUST become visible atomically or not at all.
- **FR-020**: Publication MUST compare the expected definition head under serialization and MUST preserve a rejected draft when the expected head is stale.
- **FR-021**: Exact-version dependency graphs MUST be acyclic; a rejection MUST report the complete cycle path.
- **FR-022**: The system MUST expose canonical direct dependency edges as authoritative facts; reverse and transitive dependency views MUST be derived and rebuildable.
- **FR-023**: A version comparison MUST classify public-contract, default, outcome, durability, provider, implementation, and exact-dependency changes and state the minimum required semantic-version increment.
- **FR-024**: The minimum increment MUST be major for removed or renamed contract members/outcomes, reference-key changes, incompatible type changes, optional-to-required changes, nullability tightening, new required outputs, weaker durability, changed or removed defaults, or newly emitted outcomes.
- **FR-025**: The minimum increment MUST be minor for compatible optional/defaulted inputs, compatible optional outputs, and nullability relaxation under the platform baseline, and patch for non-behavioral presentation-only changes.
- **FR-026**: Published activity versions, templates, source references, and consuming workflow executables MUST never be mutated by an upgrade.
- **FR-027**: Upgrade planning MUST order dependency changes bottom-up and pin every proposed draft revision and definition head; application MUST be atomic for the selected closure.

#### Graph-backed execution

- **FR-028**: The visual-graph provider MUST compile reusable behavior to a Runtime-owned graph activity that executes as an ordinary composite activity inside the current workflow execution.
- **FR-029**: Executing a graph-backed activity MUST NOT create a child workflow instance, child workflow actor, or separate workflow execution identity.
- **FR-030**: The outer graph activity MUST remain an explicit inspectable activity execution boundary whose descendants are ordinary activity executions.
- **FR-031**: Template placement MUST derive deterministic, collision-resistant executable-node and resume-target identities from the full invocation origin while retaining separate readable provenance.
- **FR-032**: Repeated placement and nesting of the same template MUST produce distinct placement identities without changing the template's content identity.
- **FR-033**: Foundation MUST NOT impose arbitrary default limits on graph depth, node count, or artifact size; it MUST measure resource use, honor cancellation, permit host or tenant admission policy, and avoid call-stack-dependent traversal.
- **FR-034**: Before any descendant is scheduled, the outer activity MUST evaluate and durably capture all effective public inputs exactly once, initialize graph-local state, defer its own completion, and commit graph-entry scheduling atomically.
- **FR-035**: A graph activity MUST break the caller's user-variable scope chain, expose captured public inputs as read-only values, and initialize graph-local durable variables once per activity execution.
- **FR-036**: Graph descendants MUST retain permitted ambient runtime capabilities such as identity, tenant, services, tracing, time, and cancellation while being denied workflow-root mutation and trigger-entry behavior by default.
- **FR-037**: Public outputs MUST be produced only through compiled boundary mappings; successful completion MUST fail when any required output is missing or cannot be durably captured.
- **FR-038**: Natural graph completion MUST emit `Done`; explicit return and multiple-outcome semantics are deferred but MUST remain scoped to the nearest graph boundary when introduced.
- **FR-039**: Boundary output capture, outcome recording, outer terminalization, and parent continuation MUST commit atomically.

#### Suspension, faults, cancellation, retries, and recovery

- **FR-040**: Graph execution MUST use the existing activity pipeline, child scheduling, checkpoint, post-commit intent, bookmark, incident, cancellation, and recovery contracts.
- **FR-041**: Descendant bookmarks MUST remain owned by the descendant activity executions that created them; the outer activity MUST NOT create proxy bookmarks.
- **FR-042**: A complete host teardown and restart MUST preserve graph suspension and resume the exact committed descendant without replaying committed work.
- **FR-043**: Internal incidents MUST remain inspectable and a causally linked outer-boundary fault MUST communicate failure to the caller.
- **FR-044**: Cancellation MUST durably clean descendant bookmarks, timers, and pending work before terminalizing the outer activity; racing resume and cancellation MUST be resolved by the first committed transition.
- **FR-045**: Retrying a failed graph activity MUST create a fresh outer execution scope and fresh descendants while preserving the pinned template and the effective input snapshot, with first-attempt and previous-attempt provenance.
- **FR-046**: The feature MUST NOT claim exactly-once external side effects across retries.
- **FR-047**: Missing or unsupported Runtime consumers required by a retained artifact MUST produce an artifact-activation incident and MUST be detectable by deployment preflight.
- **FR-048**: Runtime execution MUST require only the runnable artifact, its live source reference where reference policy is required, configured Runtime consumers, and Runtime state; it MUST NOT load Activity or Workflow Design data.

#### Inspection, security, lifecycle, and migration

- **FR-049**: Runtime inspection MUST let an authorized consumer expand a graph-activity execution into a hierarchical, lazily loaded, cursor-paginated execution view.
- **FR-050**: Inspection MUST expose the outer lifecycle separately from a derived subtree aggregate, stable execution and attempt identities, causal provenance, bookmark and incident summaries, outcome evidence, child counts, and expansion availability.
- **FR-051**: Inspection MUST render from the pinned hierarchical layout carried by the executed source reference and MUST NOT consult current Design state.
- **FR-052**: Inspection authorization MUST distinguish structure access from sensitive-value access and MUST reapply tenant authorization and redaction to every page and expansion.
- **FR-053**: Tenant-authored definitions MAY reference same-tenant or global definitions and MUST NOT reference another tenant's definitions, even by exact identifier.
- **FR-054**: Retirement MUST block new direct selection without invalidating already closed parent templates; revocation MUST remain a distinct stronger lifecycle action.
- **FR-055**: Draft test runs MUST use the normal artifact store and Runtime pipeline through an expiring source reference to a synthetic wrapper workflow.
- **FR-056**: The public backend surface MUST expose coherent authoring, validation, publication, version-comparison, dependency, upgrade-planning, test-run, preflight, and inspection operations with stable machine-readable errors.
- **FR-057**: Validation failures MUST identify a stable code, severity, affected subject and location, human-readable message, and safe remediation context without leaking protected provider payloads or values.
- **FR-058**: Elsa 3 conversion MUST separate analysis from application, generate deterministic identities and exact reference rewrites, create both an activity definition and wrapper workflow for reusable workflows, and apply selected dependency closures atomically.
- **FR-059**: Elsa 3 recursive reusable composition MUST be rejected with a complete cycle path and MUST NOT be silently converted to separate-workflow execution.
- **FR-060**: The existing Foundation workflow-as-activity surface MUST be removed, including its marker, backing activity, and catalog/reconciliation path; the explicit separate-workflow execution activity MUST remain available.
- **FR-061**: Architecture verification MUST reject new Runtime dependencies on Activity Design, Workflow Design, or Publishing implementation packages.
- **FR-062**: An authorized caller MUST be able to fork an exact source-owned activity version into a new tenant-owned, Design-authoritative definition and draft without mutating or competing with the source lineage.

### Key Entities *(include if feature involves data)*

- **Activity Definition**: Stable Activity Catalog identity and lineage for one reusable activity contract.
- **Activity Definition Draft**: Mutable provider-neutral contract plus provider manifest, optimistic revision, optional source-version lineage, content authority, tenant, and validation state.
- **Activity Definition Version**: Immutable semantic version of an activity definition with an authoritative public contract, provider identity, exact dependencies, and executable-template reference.
- **Activity Public Contract**: Provider-neutral inputs, outputs, outcomes, stable reference keys, type references, defaults, independent requiredness and per-member nullability, durability policy, and presentation metadata.
- **Provider Manifest**: Provider-owned authored implementation payload identified by stable provider key and schema version.
- **Executable Activity Template**: Content-addressed, immutable, deterministic Runtime execution material with exact closed dependencies and Runtime consumer requirements.
- **Activity Dependency Edge**: Immutable direct edge from an owning activity version/template to an exact dependency version/template, including authored origin.
- **Activity Version Diff**: Comparison result classifying contract, behavior, provider, and dependency changes and the minimum semantic-version increment.
- **Activity Upgrade Plan**: Bottom-up proposed changes to activity and workflow drafts, pinned to observed revisions and definition heads.
- **Graph Activity Execution Scope**: The ordinary outer activity execution and its isolated durable values, descendant executions, attempt lineage, and boundary state; not a separate persisted invocation entity.
- **Hierarchical Activity Inspection View**: Runtime-owned projection that connects an outer activity execution to paged descendant evidence and pinned layout without requiring Design data.
- **Validation Diagnostic**: Stable, machine-readable explanation of an authoring, publication, dependency, provider, activation, admission, or migration failure.
- **Content Authority**: The one source allowed to create or mutate content for a definition lineage.
- **Source Reference**: Provenance, scope, lifetime, and layout sidecar record pointing to content-addressed execution material.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The release-gate restart scenario publishes, executes, suspends, fully restarts against the same durable state, resumes, completes, and inspects a graph-backed reusable activity using exactly one workflow execution identity and no Design store at runtime.
- **SC-002**: In the release-gate scenario, effective inputs are captured once, one descendant bookmark resumes without replay, and the public output is propagated exactly once at the activity boundary.
- **SC-003**: Repeated placement of the same template produces unique descendant and resume-target identities in 100% of characterization cases while identical behavioral templates retain identical content hashes.
- **SC-004**: Publication failure at every gated phase leaves zero partially visible versions, heads, templates, source references, or authoritative dependency edges.
- **SC-005**: All tested breaking and compatible contract changes produce the agreed minimum semantic-version classification, and an insufficient requested version is rejected.
- **SC-006**: Existing published workflows retain the same selected activity-version identity and behavior after newer versions, retirement, source deletion, provider migration, and upgrade-plan creation.
- **SC-007**: A nested execution containing at least 10,000 committed descendant executions can be inspected through bounded pages without requiring one unbounded response; every returned page remains tenant-authorized and value-redacted according to policy.
- **SC-008**: Cancellation/resume race tests produce exactly one winning durable terminal path in every forced ordering, with no remaining descendant bookmark, timer, or pending work after cancellation wins.
- **SC-009**: Deployment preflight identifies every missing Runtime consumer required by the active retained-artifact fixture before dispatch, and activation reports the same missing requirement as a non-retryable deployment incident.
- **SC-010**: Architecture guards find zero new Runtime-to-Design or Runtime-to-Publishing implementation references.
- **SC-011**: An Elsa 3 conversion fixture with reusable references and direct starts produces deterministic results on repeat analysis/application, while missing references and cycles cause zero partial writes.
- **SC-012**: A provider author can describe a second implementation provider using only the published provider, validation, compilation, manifest, Runtime-consumer, and conformance contracts, with zero changes required to universal activity-version or Runtime dispatch models.

## Assumptions

- The active program-goal owner is [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md); the feature also changes Activity Design and Publishing bridges while preserving Runtime ownership of execution behavior.
- The constitution is draft/provisional. This work treats the current Design/Runtime split, Activity Catalog authority, artifact-only runtime, workflow state triplet, and naming rules as binding plan gates; a contradictory ratification change would require replanning.
- Existing content-addressed workflow artifact, Source Reference, layout-sidecar, checkpoint, pipeline, activity-execution inspection, and Groundwork seams are extended rather than duplicated.
- The first provider is the visual activity graph. Future JavaScript, C#, remote, and similar providers are contract consumers, not first-slice implementations.
- The clean break applies to the pre-release Foundation workflow-as-activity surface. Compatibility work targets one-way Elsa 3 import only.
- Host or tenant admission policy may reject material that is too expensive for its environment; Foundation supplies measurements and policy seams rather than universal size ceilings.
- The canonical highest-level acceptance gate uses a durable store and complete host recreation; the implementation plan selects the concrete conformance store.

## Out of Scope

- Elsa Studio screens, navigation, wireframes, interaction design, accessibility, and visual design.
- Creating another workflow execution for graph-backed reusable activity behavior.
- Backward compatibility for Foundation's existing workflow-as-activity types or persisted rows.
- Trigger-capable reusable activities in the first implementation slice.
- Shipping JavaScript, C#, remote, or other new implementation providers in the first slice.
- A public reusable provider test SDK before a second provider exists.
- Explicit return and multiple-outcome execution in the first slice, beyond preserving compatible backend contract space.
- Automatic conversion of recursive composition into separate-workflow execution.
- Exactly-once external effects across retries.
- In-place mutation or recompilation of published activity versions, templates, source references, or workflow executables.

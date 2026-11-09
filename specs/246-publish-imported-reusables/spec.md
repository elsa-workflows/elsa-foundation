# Feature Specification: Publish imported Elsa 3 reusable activities so they run as graph activities

**Feature Branch**: `2796-publish-imported-reusables`

**Created**: 2026-10-15 · **Revised**: 2026-10-19 (re-read against main at 9436d2193)

**Status**: Implemented — merged as [#3031](https://github.com/elsa-workflows/elsa-foundation/pull/3031), 2026-10-20

**Issue**: [#2796](https://github.com/elsa-workflows/elsa-foundation/issues/2796)

**Builds on**: spec [236](../236-elsa3-activity-map/spec.md) (#2706, merged as #2805, 4b902aaa2), spec
[239](../239-elsa3-flowchart-connections/spec.md) (#2705, merged as #2857, b0aea33a6), spec
[238](../238-flowchart-trigger-start/spec.md) (merged as #2856, 6b3ad8c3d) and the `IElsa3NodeConverter` contract
(#2845). Completes 236's deferred compile proof. Spec 236's SC-006 (a caller receives a reusable's output) moves to
the follow-up spec for engine intrinsics in graph activities (D-7).

**Runs alongside**: #2717 (spec 247). The two touch shared files only: diagnostic codes, the Mapping README,
`EXTENSION_POINTS.md` and the generated maps.

**Input**: Issue #2796 and spec 236's handover comment on it. The decisions under [Decisions](#decisions) come from a
review with the maintainer on 2026-10-15; D-5 and D-6 were added in the 2026-10-19 review against main, and D-7 during
implementation on 2026-10-19.

## Context (verified on main at 9436d2193, 2026-10-19; line numbers drift)

- The Elsa 3 collection import writes each reusable activity version straight into the catalog.
  `Elsa3ReusableActivityImportMaterializer.BuildActivity` builds an `ActivityDefinitionVersion` with the schema-2 graph
  manifest (`BuildGraphManifest`) as `DescriptorPayload`, `DesignFacets = []` and no outcomes. It sets the authoring
  head to the newest selected version. `EfReusableActivityImportCommand` commits bindings, the receipt, workflows,
  definitions, versions, authoring state and the management projection in one `EfSharedTransaction`. It writes no
  draft, draft layout, publication, layout, compiled template, dependency edge or source reference.
- `WorkflowExecutableCompiler` treats a version that has no publication as a plain authorable row. The graph runtime
  descriptor needs `definitionId`, `definitionVersionId`, `version`, `templateHash` and an entry node, which
  `ActivityTemplatePlacer` stamps from a publication and its template. So a workflow that uses an imported reusable
  activity cannot run it, and the import's own direct-start wrapper cannot run it either.
- Studio and REST publish a reusable activity in two steps on one Design-owned draft: `PreflightAsync` returns a review
  token, then `PublishReviewedAsync` re-evaluates, checks the token and commits. `ActivityDefinitionPublisher`
  validates and compiles the draft through the graph provider and writes the catalog version (its payload is the
  compiled template root), the `elsa.outcomes` facet from the contract, the publication (with contract), the layout,
  dependency edges, the compiled template and the source reference.
- Compiling a graph activity needs every dependency already published (a publication plus a template): imported
  reusables it calls, and every built-in it uses. Since #2857 a converted reusable body can be a flowchart, so that
  includes the Flowchart activity, Sequence, the graph `Complete` activity and each CLR leaf. Reconciliation publishes
  the built-ins as source-owned publications only when the host has the Workflows Publishing API feature. Nothing
  publishes a batch in dependency order today, and no test compiles a converted manifest.
- Constraints the publisher puts on the import:
  - It needs a draft **and a draft layout** for the definition (`activity.draft.not-found`,
    `activity.draft.layout-not-found`).
  - It runs a three-step, forward-converging commit (Runtime template, then the Activities Design linearization point,
    then the Publishing receipt) under a distributed lock, so it cannot join the import's shared transaction.
  - It assigns a fresh `activity-ver-*` id, but the import's wrappers and callers reference deterministic version ids
    from the plan. No publish request accepts a version id today.
  - It refuses a definition whose authoring head has no publication (`activity.definition.head-invalid`). Every
    definition the import writes today has exactly such a head.
  - Its commit refuses a catalog version that already exists by id or by SemVer sort key.
  - The publish endpoint authorizes against `WorkflowPublishingPermissions.Manage` (preflight needs only `Read`). The
    tenant comes from the caller's claims. Import apply checks only `Elsa3ImportPermissions.Manage` and needs an
    explicit tenant scope.
- Outcomes come from two places. The contract outcomes are `Elsa3ConvertedDefinition.BoundaryOutcomes`, assembled from
  the `Complete` activities the terminal rewrite places, with `done` always present. A caller's flowchart routes on
  `Elsa3ActivityExactReplacement.OutcomeNames` (new in #2857), which the materializer reads separately from the
  target's Elsa 3 JSON (`CompletionOutcomes`). Nothing checks that the two agree.
- Conversion warnings (`ELS3-BEHAVIOUR-*`) are returned on the apply result. `Elsa3ImportRecordCodec` strips them from
  the stored receipt (`Warnings = null`), so a replay or a status read loses them.
- `GraphProviderProof` (test support) runs only `ValidateAsync`. It now registers the Flowchart feature beside
  Sequence and ControlFlow, and builds the publication contract in a test helper (`Contract(converted)`).
- The start-path rule (spec 238) runs on workflow publication preflight, trigger indexing, artifact reconciliation and
  test runs. `ActivityDefinitionPublisher` does not call it, so it does not apply to publishing an activity version.
  The graph provider separately refuses a trigger capability inside a graph manifest
  (`activity.graph.trigger-entry-forbidden`).
- A reusable Elsa 3 workflow may contain a leaf start trigger (`CanStartWorkflow`). The analyzer records it in
  `plan.DirectStarts` and refuses only a trigger that owns nested behaviour (`ELS3-REUSABLE-UNSUPPORTED-TRIGGER`). The
  materializer ignores `DirectStarts`: the trigger stays in the graph body, and the wrapper starts with the graph node.
- `IElsa3NodeConverter` (#2845) is a Mapping-level replacement contract. The import uses `IElsa3DefinitionConverter`
  and does not go through it.

## Decisions

| # | Decision | Rationale |
|---|---|---|
| D-1 | **Commit, then publish.** The atomic commit writes the activity definitions, their authoring state without a head, and the workflows. A publish phase then takes each reusable version through draft → preflight → reviewed publish, dependencies first. Re-applying the same plan resumes the versions not yet published. | The publisher keeps separate stores and a forward-converging commit on purpose. Making it join the import transaction would be a large cross-lane change. |
| D-2 | **The publisher accepts a requested version id.** A publish request may name the version id to commit. The import passes the plan's deterministic id. Without one, the publisher generates an id as it does today. | Wrapper and caller references, plan reapply and idempotency stay as spec 236 built them. A caller's draft can name its dependency's final id before that dependency is published. |
| D-3 | **Warnings persist on the import receipt.** The stored receipt keeps the conversion warnings, so a replay and a status read return the same warnings as the first apply. They are not added to the publication, its contract or its facets. | They describe the conversion event, not the published artifact. This matches the scope comment on #2796. |
| D-4 | **Apply requires both import and publish permission.** `Elsa3ImportPermissions.Manage` and `WorkflowPublishingPermissions.Manage` are both checked before anything is written. | The import must not become a second way to publish that skips the publish permission. |
| D-5 | **Caller outcomes must match the published contract.** The outcomes a caller routes on and the outcomes the dependency's contract declares come from one source, or the item is refused before commit. *(2026-10-19)* | Faithful or refuse: after publish, the caller compiles against the contract, so a mismatch would fail late or route wrongly. |
| D-6 | **A reusable with a leaf start trigger is refused at analysis.** The analyzer refuses a reusable Elsa 3 workflow that contains a `CanStartWorkflow` activity, with an `ELS3-REUSABLE-*` diagnostic and guidance, before anything is written. Moving the trigger into the direct-start wrapper is left to its own Elsa 4 parity spec. *(2026-10-19)* Only a real trigger counts: on a non-trigger (a WriteLine, an If, a Flowchart, …) the flag only picks the flowchart's start node, which conversion keeps since spec 248, so the analyzer asks the activity map (`IReusableActivityTriggerClassifier`) and lets it through. A type the map does not know counts as a trigger, and its guidance says how to clear the flag. *(2026-10-20, after spec 248 merged)* | Faithful or refuse. Today the trigger stays in the graph body, where the graph provider forbids triggers and nothing would start the wrapper the way Elsa 3 started the workflow. |
| D-7 | **A reusable whose body uses an engine intrinsic is refused at conversion.** SetVariable, SetOutput, Correlate and SetName convert to Elsa 4 engine intrinsics (`elsa.intrinsic.*`). A graph activity resolves every node to a published activity version, and intrinsics have none, so such a body cannot be published. Conversion refuses it with `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED`. Intrinsic support in graph activities, and the output test it enables (spec 236 SC-006), move to a follow-up spec. *(2026-10-19, found by the compile proof)* | Faithful or refuse; a real parity gap gets its own spec. Without it, these versions would fail at publish with an unresolved-dependency error that does not say why. |
| D-8 | **A reusable whose body reads its inputs, its variables or another activity's result is refused at conversion.** In Elsa 4 a graph body node resolves its bindings against the started workflow, never the graph: `getInput` reads the started workflow's inputs, a variable read reads the started workflow's variables, and a body activity-result reference is refused at workflow publish (`VF-COER-001`). Conversion refuses `getInput`, `getVariable`, output reads and Elsa 3 memory references in a reusable body with `ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED`. Body reads of graph-scoped values move to [#3027](https://github.com/elsa-workflows/elsa-foundation/issues/3027). *(2026-10-19, found by the integration tests; a first fix through a boundary-input graph variable was tried and does not work, because body nodes cannot read graph variables either)* **Lifted by [spec 265](../265-graph-activity-scope-reads/spec.md) (#3027, 2026-10-22):** a graph activity body is now a closed scope that reads its own values, so the converter no longer refuses these reads and the code is removed. | Faithful or refuse: the import otherwise reads the wrong data silently, or hangs. |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An imported reusable activity runs in an Elsa 4 workflow (Priority: P1)

A migration engineer imports an Elsa 3 collection that contains a reusable workflow. They then run an Elsa 4 workflow
that uses the imported activity and see it follow the outcome the activity completed with.

**Why this priority**: This is the defect. Without it, imported reusable activities exist in the catalog but cannot
run, so the collection import does not migrate reusable behaviour at all.

**Independent Test**: Import a collection with reusable workflows that complete with different outcomes, and caller
flowcharts that branch on those outcomes. Run each caller and check the branch it takes.

**Acceptance Scenarios**:

1. **Given** an applied import of a reusable Elsa 3 workflow, **When** the activity catalog is read, **Then** each
   imported reusable version has an active publication. The publication's contract lists the converted inputs, outputs
   and outcomes, the version carries an `elsa.outcomes` facet and a compiled template exists.
2. **Given** that published activity, **When** a workflow that calls it is published and run, **Then** the caller
   runs the published graph activity, binding a value to its declared input, and completes.
3. **Given** the import's direct-start wrapper for that reusable workflow, **When** it runs, **Then** it runs the
   published activity and completes.
4. **Given** a reusable workflow whose Elsa 3 body ends with more than one outcome, **When** it is published,
   **Then** the contract and the `elsa.outcomes` facet carry `done` and each converted outcome, under the names the
   conversion produced.
5. **Given** a reusable workflow whose Elsa 3 body is a flowchart, **When** the import is applied, **Then** it is
   published and compiled like a sequence-bodied one. 
6. **Given** an imported caller whose flowchart routes on a non-`done` outcome of an imported reusable, **When** the
   caller is run, **Then** it follows the connection for the outcome the reusable completed with.
7. **Given** a reusable workflow whose body uses SetVariable, SetOutput, Correlate or SetName, **When** the import is
   analyzed and applied, **Then** its conversion is refused with `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED`, naming
   the node, and nothing is written (D-7).
8. **Given** a reusable workflow whose body reads one of its inputs, a variable or another activity's result, **When**
   the import is applied, **Then** its conversion is refused with `ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED`, and *(Lifted by spec 265, #3027: the code is removed and these reads convert.)*
   nothing is written (D-8).

---

### User Story 2 - Reusable activities that use each other are published in dependency order (Priority: P1)

A collection contains reusable workflows that use other reusable workflows from the same collection, and several
versions of one reusable workflow.

**Why this priority**: Compilation refuses an unpublished dependency, so real collections fail without an order.

**Independent Test**: Import a collection where reusable A uses reusable B, and B has two selected versions. Check
that every version is published and that A compiles against the B version its plan names.

**Acceptance Scenarios**:

1. **Given** reusable A uses reusable B, **When** the import is applied, **Then** B is published before A and both
   publications are active.
2. **Given** two selected versions of one reusable workflow, **When** the import is applied, **Then** they are
   published in ascending version order, and the authoring head ends on the highest one.
3. **Given** an applied import, **When** the published versions are read, **Then** each has the version id the plan
   assigned, so wrappers and callers resolve to them unchanged.

---

### User Story 3 - A failed or interrupted publish is visible and resumable (Priority: P2)

Publishing one item fails, or the process stops part way. The engineer sees which items are published and which are
not, and re-applies the same plan to finish.

**Why this priority**: Publishing happens after the atomic commit, so partial state is possible and must not be
silent.

**Independent Test**: Make one item's publish fail. Check the apply result and the stored receipt, fix the cause,
re-apply, and check that the remaining items are published and nothing is published twice.

**Acceptance Scenarios**:

1. **Given** one reusable version is refused by publish validation, **When** the import is applied, **Then** the apply
   reports that item as not published with the publisher's diagnostics, and items that do not depend on it are still
   published. Items that depend on it are reported as not published because of their dependency.
2. **Given** a partly published import, **When** the same plan is applied again, **Then** only the missing versions are
   published, and versions already published are left as they are.
3. **Given** a fully published import, **When** the same plan is applied again, **Then** the result reports
   already imported, nothing is republished, and the warnings and publication state match the first apply.

---

### User Story 4 - Conversion warnings survive replay and status reads (Priority: P2)

An engineer applies an import that produced `ELS3-BEHAVIOUR-*` warnings, then reads the import status later or
replays the apply.

**Why this priority**: The warnings describe run-time differences the engineer must review. Losing them after the
first response hides that.

**Independent Test**: Apply an import that produces a behaviour warning. Read the status and replay the apply, and
compare the warnings with the first response.

**Acceptance Scenarios**:

1. **Given** an applied import with conversion warnings, **When** its status is read or the apply is replayed,
   **Then** the same warnings are returned.
2. **Given** a published imported activity, **When** its publication, contract and facets are read, **Then** they
   contain no conversion warnings.

---

### User Story 5 - Applying an import needs permission to publish (Priority: P2)

A user with the import permission but not the publish permission tries to apply an import.

**Why this priority**: Applying now publishes, and must not bypass the publish permission.

**Independent Test**: Apply an import as a caller without the publish permission.

**Acceptance Scenarios**:

1. **Given** a caller with the import permission but not the publish permission, **When** they apply an import,
   **Then** the apply is refused before anything is written.
2. **Given** a caller with both permissions, **When** they apply, **Then** the import is committed and published in
   the caller's tenant.

### Edge Cases

- A reusable activity uses a built-in activity that has no publication in this host (for example, reconciliation has
  not published it yet): that item is refused with the publisher's dependency diagnostic, not with an exception.
- A plan whose version id is already used by a different, non-imported version is refused, as spec 236's collision
  rules refuse it today.
- A requested version id that already exists for the same draft content is treated as already published, not as a
  conflict, so reapply stays idempotent.
- The authoring head of a definition is not advanced by the commit. Until its first version is published, the
  definition has no head and callers cannot pick it.
- A selected version lower than a version of that definition that is already published is reported as not published,
  with the publisher's `activity.version.bump-insufficient`. The publisher only publishes ascending versions.
  *(amended 2026-10-19, research R-3)*
- Elsa 3 dependency cycles are already refused by the analyzer before apply, so the publish order never meets one.
- The non-reusable (direct-start) workflows and the wrappers are committed as today. This spec does not publish
  workflow definitions. When a user later publishes or activates one, it goes through the start-path rule of spec
  238 like any other workflow; the wrapper's root is the graph node, so it holds no trigger of its own.
- A caller cannot route on an outcome the dependency's contract does not declare (D-5): the caller's outcomes are
  read from the converted dependency's `BoundaryOutcomes`, so the two cannot disagree (research R-9).
- A reusable body with a leaf start trigger is refused at analysis (D-6), so it never reaches commit or publish. A
  trigger that owns nested behaviour keeps its existing `ELS3-REUSABLE-UNSUPPORTED-TRIGGER` refusal.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Applying an Elsa 3 collection import MUST publish every selected reusable version through a
  Design-owned draft and `ActivityDefinitionPublisher` (preflight, then reviewed publish). Nothing may write its
  catalog version, publication or template directly.
- **FR-002**: The draft for an imported version MUST carry the schema-2 graph manifest from
  `Elsa3ReusableActivityImportMaterializer.BuildGraphManifest` as its provider manifest, and a draft layout as the
  publisher requires. Its contract MUST carry the converted inputs, outputs and outcomes
  (`Elsa3ConvertedDefinition.BoundaryOutcomes`). This logic moves from the test helper `GraphProviderProof.Contract`
  into the import, and the test helper uses the production code.
- **FR-003**: The resulting publication MUST carry that contract, a compiled template and an `elsa.outcomes` facet
  listing the boundary outcomes, as the publisher produces them for a Studio or REST publish.
- **FR-004**: The atomic import commit MUST no longer write a catalog version for an imported reusable activity, and
  MUST NOT set an authoring head to an unpublished version. Definitions, authoring state, bindings, the receipt and
  workflows are still committed atomically as spec 236 left them.
- **FR-005**: The publish phase MUST publish dependencies before the activities that use them, and versions of one
  definition in ascending version order.
- **FR-006**: `ActivityDefinitionPublisher` MUST accept an optional requested version id on a publish request and
  commit under that id. With none, it behaves as today. A requested id already used by another version MUST be
  refused with the existing version-conflict error.
- **FR-007**: The import MUST request the plan's deterministic version id for each version it publishes.
- **FR-008**: The publish phase MUST be idempotent per version: a version that is already published with the same
  content is skipped, and its existing publication is reported.
- **FR-009**: Re-applying the same plan MUST resume publishing any selected version not yet published. Because a
  completed apply deletes its upload, re-applying means applying the re-uploaded export under a new idempotency key:
  its commit reuses the imported rows and its publish phase publishes only what is missing. A replay of the same key
  returns the stored receipt with each version's live publication state. *(amended during implementation;
  accepted by the maintainer, 2026-10-19)*
- **FR-010**: The apply result (first apply and replay) MUST report, for each reusable version, whether it is
  published, and for one that is not, the publisher's error code and diagnostics. An item not published because a
  dependency was not published MUST say so. A status read MUST report whether each version is published, read live
  from the publication store. The stored receipt does not record publication state. *(amended 2026-10-19, research
  R-6)*
- **FR-011**: A version refused by the publisher MUST NOT stop the publishing of versions that do not depend on it.
- **FR-012**: The stored import receipt MUST keep the conversion warnings. Replays and status reads MUST return the
  same warnings as the apply that committed.
- **FR-013**: Conversion warnings MUST NOT be written to the publication, its contract or its facets.
- **FR-014**: Applying an import MUST require `WorkflowPublishingPermissions.Manage` in addition to
  `Elsa3ImportPermissions.Manage`, checked before anything is written. Publishing MUST run in the import's tenant,
  which MUST be the caller's tenant.
- **FR-015**: The provider proof in the Mapping tests (`GraphProviderProof`) MUST add a `CompileAsync` assertion for
  converted reusable workflows, with dependencies resolved from `RealActivityCatalog`, extended into
  `Elsa3ReusableTerminalTests`, `Elsa3CollectionLoweringTests` and the flowchart-bodied reusable cases in
  `Elsa3FlowchartConversionTests`.
- **FR-016**: A run-time test MUST import a reusable Elsa 3 workflow, publish and run a workflow that calls the imported
  activity, and assert that it ran: a caller's flowchart routes on the non-`done` outcome the reusable completed with.
  Asserting a reusable's output moves to #3023 (D-7); a body computing from its inputs moves to #3027 (D-8). The tests run in an in-process Elsa 4
  host and upload test-owned Elsa 3 export JSON through the import API; no Elsa 3 instance is involved. *(amended
  2026-10-19, research R-13)*
- **FR-017**: No Runtime project may gain a dependency on a Design project.
- **FR-018**: The outcomes a caller's converted flowchart routes on for an imported reusable (`OutcomeNames`) MUST
  equal that reusable's `BoundaryOutcomes`. The import derives both from one source (research R-9), so no mismatch
  diagnostic is needed.
- **FR-019**: The analyzer MUST refuse a reusable Elsa 3 workflow that contains a leaf `CanStartWorkflow` trigger (an activity
  the activity map does not know as a non-trigger),
  with an `ELS3-REUSABLE-*` diagnostic that names the node and gives guidance, before anything is written (D-6).
  
- **FR-020**: The Mapping README MUST drop the note that imported reusable activities are not yet published, and
  document the publish phase.
- **FR-021**: Conversion of a reusable workflow MUST refuse every node that resolves to an engine intrinsic with
  `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED`, naming the node and the Elsa 3 type, with guidance (D-7). Non-reusable
  workflows keep converting them.
- **FR-022**: Conversion of a reusable workflow MUST refuse a body that reads one of its inputs, a variable or another
  activity's result (`getInput`, `getVariable`, output reads, Elsa 3 memory references) with
  `ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED` (D-8). Non-reusable workflows keep converting them. *(Lifted by spec 265, #3027: the code is removed and these reads convert.)*

### Key Entities

- **Imported reusable version**: one selected Elsa 3 reusable workflow version. After apply it is a published
  activity version with the plan's deterministic id, a publication, a contract (inputs, outputs, outcomes) and a
  compiled template.
- **Import draft**: the Design-owned draft the import creates for each version: the schema-2 graph manifest, a draft
  layout and the contract. The publisher consumes it.
- **Import receipt**: the durable record of an apply. It gains the conversion warnings. The apply, replay and status
  responses add each reusable version's publication state, which is never stored.
- **Requested version id**: an optional field on a publish request naming the id the publisher must commit under.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every reusable version in an applied import fixture has an active publication with a compiled template.
  None is left as an unpublished catalog row.
- **SC-002**: A workflow calling an imported reusable activity runs to completion, and a caller routing on a non-`done`
  outcome follows the connection for the outcome the reusable completed with.
- **SC-003**: `GraphProviderProof` compiles every converted reusable fixture that it validates today, flowchart bodies
  included, with zero error diagnostics.
- **SC-004**: Re-applying an applied plan publishes nothing new and returns the same warnings and publication state as
  the first apply. Re-applying a partly published plan publishes exactly the missing versions.
- **SC-005**: A caller without the publish permission cannot apply an import, and no row is written.
- **SC-006**: A Studio or REST publish without a requested version id behaves exactly as before. The existing publisher
  tests pass unchanged.

## Assumptions

- The host that applies an import also hosts Workflows Publishing and its API feature (the publisher, the template
  compiler, the Runtime template store and source-owned publication of built-ins during reconciliation), plus the
  `Elsa3ImportActivitiesPublishing` bridge feature. Without the bridge, an apply that selects a reusable version is
  refused before anything is written, with `ELS3-REUSABLE-PUBLISHING-UNAVAILABLE` (research R-1).
- Built-in activities used by a converted body (Flowchart, Sequence, graph `Complete`, CLR leaves) are already
  published by reconciliation in that host, as they are for Studio-authored graphs.
- The direct-start wrappers stay Design workflow rows. Publishing workflow definitions is out of scope.
- The publish phase runs in the apply request, not as a background job. A collection large enough to need a
  background job is out of scope.
- Imports applied before this change (unpublished catalog rows with a head) are not upgraded in place. Elsa 4 is
  pre-GA (ADR 0080); such a store is re-imported. *(confirmed 2026-10-19)*

## Out of Scope

- Publishing imported workflow definitions, wrappers included.
- Moving a reusable's start trigger into its wrapper (D-6); this needs its own Elsa 4 parity spec.
- Engine intrinsics in graph activities (D-7), and with them reusable outputs set by SetOutput and spec 236's SC-006;
  these need their own Elsa 4 spec ([#3023](https://github.com/elsa-workflows/elsa-foundation/issues/3023)).
- Graph body nodes reading the activity's inputs, its variables or other body results (D-8,
  [#3027](https://github.com/elsa-workflows/elsa-foundation/issues/3027)).
- Surfacing conversion warnings in Studio's activity views.
- Changing how Studio or REST choose version ids.
- Routing the import through `IElsa3NodeConverter` (#2845).
- Upgrading stores imported before this change.
- #2717 (spec 247) and spec 235 (Elsa3JsonReconciliation).

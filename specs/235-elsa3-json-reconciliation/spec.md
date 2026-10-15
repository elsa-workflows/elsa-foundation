# Feature Specification: Elsa3JsonReconciliation — convert Elsa 3 workflow JSON to Elsa 4 at engine start

**Feature Branch**: `235-elsa3-json-reconciliation`

**Created**: 2026-10-13

**Status**: Draft

**Tracking**: ADO 47607, #2707 (prerequisites #2705, #2706)

**Input**: User description: "Elsa 3 workflow definitions (JSON) must convert to Elsa 4 automatically at engine
start, through the existing reconciliation mechanism, with no HTTP import and no Studio step. We use this to run
one engine-neutral JTest suite collection against Elsa 3 and then, unchanged, against Elsa 4 with the converted
workflows. So the conversion runs on every start, and every suite run also tests the converter."

## Context (verified against origin/main 0602710db, 2026-10-13; line numbers drift)

- **Reconciliation sources.** `IActivityReconciliationSource` (`Elsa.Activities.Design.Core`, namespace
  `…Core.Reconciliation`) exposes `SourceId`, `SourceKind`, `Read`. `IWorkflowReconciliationSource`
  (`Elsa.Workflows.Design.Reconciliation`) adds `RequestsPublication` (default `false`).
- **Pass order.** `ActivityVersionReconcilerStartupTask` is `[Order(1)]`, `WorkflowsVersionReconcilerStartupTask`
  is `[Order(2)]`; `TaskManager` sorts every startup task in a shell together, so activities reconcile before
  workflows on every start, on every node.
- **One activity pass reads all sources before writing.** `CollectActivityVersions` reads every
  `IActivityReconciliationSource`, then `ActivityVersionReconciler` writes. A source cannot see what another
  source contributes in the same pass. The activity reconciler never resolves references between activities, so
  graph activities that reference each other can arrive in any order within one pass.
- **Ids.** `CollectActivityVersions` assigns version ids with the public `ActivityCatalogStableIds.VersionId(
  activityTypeKey, version)` (#2668, PR #2699); definition ids fall back to `ActivityCatalogStableIds.DefinitionId`.
- **Content changes.** The activity reconciler throws `ActivityVersionHashMismatchException` when a stored
  version (matched by SemVer sort key, build metadata ignored) has a different content hash; a difference only
  in build metadata is skipped silently. The workflow reconciler logs a content mismatch and keeps the stored
  version, and skips versions older than the stored latest.
- **Removal.** Activity versions are never deleted ("source disappearance is intentionally not tracked").
  Workflow definitions are soft-deleted only when a source explicitly sends `Deleted = true`; workflow source
  identity is not persisted.
- **Publication.** `RequestsPublication` flows into `WorkflowVersionsReconciled`, consumed by
  `PublishReconciledWorkflowVersions` in `WorkflowsPublishing` (`DependsOn = { WorkflowsRuntimeTriggers, Events }`).
- **Tenant.** Reconciliation carries no tenant; writes take the shell's ambient persistence scope.
- **Elsa 3 conversion today** (`Extensions/Elsa3`):
  - `ReusableActivityCollectionAnalyzer` (`Elsa3.Activities.Design.Import`) is pure, takes a plain
    `ReusableActivityImportCollection`, detects `Options.UsableAsActivity`, resolves references, reports cycles
    (`ELS3-REUSABLE-DEPENDENCY-CYCLE`) and per-item dependencies; it assigns reference ids with
    `ReusableActivityImportIdentity`.
  - `Elsa3ReusableActivityImportMaterializer` (`Elsa3.Mapping`) builds the graph activity
    (`elsa.activity-graph` / `elsa.graph-activity`, graph manifest descriptor) and the wrapper workflow in
    **private** helpers (`BuildActivity`, `BuildWrapperState`) that emit persistence entities, not reconciliation
    models. The wrapper drops the analyzer's recorded `DirectStarts`.
  - `Elsa3.Mapping` references `Elsa3.Activities.Design.Import`, which carries AspNetCore and NativeEndpoints.
  - The converter resolves each node's activity from the catalog by the Elsa 3 type name and `"{n}.0.0"`. It
    throws on Flowchart `Connections`, has no Elsa 3 → Elsa 4 type/member map, and the collection path skips
    value-flow lowering. See Dependencies.
  - The live-instance migration boundary (`Elsa3MigrationBoundary`, spec 014) refuses Elsa 3 instance state.
- **The HTTP/Studio collection import** (`Elsa3ImportJsonActivities`) persists Design-authored activities
  (content authority `elsa3.collection-import`) and unpublished workflows. It stays as it is, apart from sharing
  extracted code (FR-016) and the wrapper direct-start fix (FR-010).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Elsa 3 folder becomes runnable Elsa 4 workflows at start (Priority: P1)

A test engineer points an Elsa 4 host at a folder of Elsa 3 workflow JSON and starts it. Without any import call
or Studio step, every convertible Elsa 3 workflow is present in Elsa 4: reusable workflows as graph activities in
the activity catalog, all other workflows plus a wrapper per reusable workflow as published, runnable workflow
definitions, keeping their Elsa 3 definition ids.

**Why this priority**: This is the whole purpose: running the same engine-neutral JTest suite against Elsa 3
and, unchanged, against Elsa 4.

**Independent Test**: Compose a host with `Elsa3JsonReconciliation` on an empty store, configured with a folder
holding a plain workflow, a reusable workflow, a reusable workflow that uses another reusable workflow, and a
workflow that uses the reusable one. Start it and assert the catalog, the publications, reference resolution,
and that the plain and wrapper workflows run to completion when started by their Elsa 3 definition ids.

**Acceptance Scenarios**:

1. **Given** an empty store and that folder, **When** the host starts, **Then** each reusable workflow exists as
   an activity definition version with the graph provider/consumer keys and version `N.R.0`, and every other
   workflow and every wrapper exists as a workflow definition version `N.R.0` with an Active publication.
2. **Given** the started host, **When** every activity node in every converted workflow and graph activity is
   resolved against the catalog, **Then** every reference resolves, and each reference to a converted reusable
   workflow carries `ActivityCatalogStableIds.VersionId(activityTypeKey, "N.R.0")`.
3. **Given** the started host, **When** the plain workflow and a wrapper are started by their Elsa 3 definition
   ids, **Then** both run to completion, and the wrapper's run executes the reusable workflow's graph, including
   the nested reusable workflow.
4. **Given** reusable workflows that reference each other in any file order, **When** the host starts, **Then**
   all of them are reconciled in one start.

---

### User Story 2 - Restarting with the same input changes nothing (Priority: P1)

The suite host restarts often (per run, per node). A second start with the same Elsa 3 input and the same
converter revision must not create, change, republish or fail anything.

**Why this priority**: The conversion runs on every start; a non-idempotent start breaks persistent hosts and
multi-node hosts.

**Independent Test**: Start twice against the same persistent store and the same folder; compare catalog rows,
workflow versions and publications.

**Acceptance Scenarios**:

1. **Given** a store reconciled from a folder, **When** the host starts again with the same folder, **Then** no
   activity version, workflow version or publication is added or changed, and startup succeeds.
2. **Given** two nodes starting concurrently against one store, **When** both reconcile, **Then** they converge
   on the same rows as one node would.

---

### User Story 3 - A converter upgrade adds new versions instead of failing (Priority: P2)

When the converter's output changes, the converter revision is bumped. A persistent host that starts with the new
converter gets new activity and workflow versions, and referencing workflows follow the new activity versions,
instead of failing on a hash mismatch or silently keeping the old content.

**Why this priority**: Without it, every converter change breaks persistent hosts at startup
(`ActivityVersionHashMismatchException`) and leaves workflows on stale content.

**Independent Test**: Reconcile with revision R, restart with revision R+1 and the same input, and assert new
`N.(R+1).0` versions exist alongside the old ones, the published workflows reference the new activity versions,
and startup succeeded.

**Acceptance Scenarios**:

1. **Given** a store reconciled at revision R, **When** the host starts at revision R+1 with the same input,
   **Then** each converted item gets a new version `N.(R+1).0`, the `N.R.0` versions stay, and startup succeeds.
2. **Given** a converter change that alters output for the golden fixtures, **When** the revision constant is not
   bumped, **Then** the golden-output test fails and names the revision constant.

---

### User Story 4 - Unconvertible input is skipped and reported, not fatal by default (Priority: P2)

Some Elsa 3 workflows use activities with no Elsa 4 counterpart, types missing from the catalog, or unsupported
constructs. The host still starts, skips those workflows and everything that depends on them, and reports each one
with a code, the file, and the cause. A strict option makes the same condition fail startup instead.

**Why this priority**: For the suite, a skipped workflow surfaces as a failing test, which tells you more than a
host that won't start; the strict option serves hosts that must not run with gaps.

**Independent Test**: Add a workflow using an Elsa 3 activity without counterpart and a workflow that references
it as a reusable activity; start, and assert both are absent, both are logged with codes, a summary line reports
two skipped items, and the rest of the folder is reconciled. Repeat with the strict option and assert startup fails.

**Acceptance Scenarios**:

1. **Given** an unconvertible reusable workflow X and a workflow Y that uses X, **When** the host starts, **Then**
   neither X, X's wrapper, nor Y is reconciled; each is logged as a structured warning naming code, source file,
   definition id, version and cause (Y's cause names X); all other items are reconciled.
2. **Given** any start, **When** reconciliation of this source finishes, **Then** one summary log line reports
   counts of converted activities, converted workflows and skipped items.
3. **Given** the strict option is enabled and any item is unconvertible, **When** the host starts, **Then** shell
   activation fails with an error listing every unconvertible item.

---

### Edge Cases

- **Dependency cycle** between reusable workflows: every workflow in the cycle and its dependents are skipped with
  the analyzer's cycle diagnostic.
- **Missing or ambiguous reference** to a reusable workflow not present in the configured input: the referencing
  workflow is skipped with the analyzer's diagnostic.
- **Same definition id and version in two files**: both items are skipped with a duplicate-input diagnostic.
- **Several Elsa 3 versions of one definition** in the input: each is converted; the workflow reconciler keeps
  only versions newer than the stored latest (existing behaviour).
- **Credential literals** in a converted node: treated as unconvertible, using the same admission rule as the
  collection import.
- **Elsa 3 workflow instance state** in the input: refused through the existing migration boundary and reported as
  unconvertible.
- **File removed** from the folder: nothing is removed from the catalog or the workflow store (existing
  reconciliation contract); documented.
- **Configured folder missing, or a file not valid JSON**: startup fails with a configuration error naming the
  path; an empty folder is logged and is not an error (mirrors `JsonWorkflowReconciliation`).
- **A reusable workflow whose activity type key already belongs to an activity definition authored by the
  collection import**: skipped with a diagnostic, together with its dependents; never appended to that definition.
- **Collection import composed in the same shell**: shell activation fails with a composition error.

## Requirements *(mandatory)*

### Functional Requirements

**Feature and configuration**

- **FR-001**: The Elsa3 extension MUST provide one shell feature, `Elsa3JsonReconciliation`, that contributes an
  activity reconciliation source and a workflow reconciliation source and needs no new HTTP endpoint, Studio step
  or dedicated startup task of its own.
- **FR-002**: The feature MUST be configured with exactly one of `FilePath`, `Files` (ordered list) or `FolderPath`
  (top-level `*.json`, ordinal file-name order), plus a required `SourceId`, validated at registration as
  `JsonWorkflowReconciliation` does.
- **FR-003**: Each file MUST hold either one Elsa 3 workflow definition or an array of them, the shape the
  collection upload accepts.
- **FR-004**: Both sources MUST report `SourceKind = "Elsa3Json"` and the configured `SourceId`.
- **FR-005**: The feature MUST declare its dependency on `WorkflowsPublishing`, and composing
  `Elsa3JsonReconciliation` with its declared dependencies MUST be sufficient for the converted workflows to be
  published and runnable. The workflow source MUST always request publication.
- **FR-006**: Within one start, both sources MUST derive their output from the same reading and analysis of the
  configured input, so the activity and workflow passes cannot disagree about which items were converted or skipped.

**Conversion and identity**

- **FR-007**: Every Elsa 3 workflow usable as an activity MUST become an activity reconciliation entry: a graph
  activity with provider `elsa.activity-graph`, consumer `elsa.graph-activity` and the graph manifest as
  descriptor, with the activity type key the collection import uses for the same Elsa 3 definition.
- **FR-008**: Every other Elsa 3 workflow MUST become a workflow reconciliation entry keeping its Elsa 3 definition id.
- **FR-009**: Every reusable workflow MUST additionally produce a wrapper workflow entry that keeps the Elsa 3
  definition id and invokes the graph activity.
- **FR-010**: The wrapper MUST preserve the reusable workflow's direct-start capability (the analyzer's recorded
  direct starts). The fix lives in the shared builder, so the collection import's wrapper gains it too.
- **FR-011**: Every activity node that references a converted reusable workflow, in workflows, wrappers and graph
  activities alike, MUST use `ActivityCatalogStableIds.VersionId(activityTypeKey, version)`. It MUST NOT use
  `ReusableActivityImportIdentity` for those references and MUST NOT duplicate the stable-id formula.
- **FR-012**: Reusable workflows that reference each other MUST be reconciled in one start regardless of file order.

**Versioning**

- **FR-013**: The Elsa 4 version of every converted activity and workflow MUST be `N.R.0`, where N is the Elsa 3
  version and R is one converter revision constant owned by the converter. R MUST start at 1.
- **FR-014**: A golden-output test MUST convert a fixed set of Elsa 3 fixtures and fail when the converted output
  differs from the recorded output while R is unchanged. Recording new output MUST require bumping R.

**Activity pass staging (core)**

- **FR-015**: `IActivityReconciliationSource` MUST gain an optional stage (default 0, so existing sources are
  unchanged). Within the single `[Order(1)]` activity pass, the reconciler MUST read and write stage by stage in
  ascending order, so a later-stage source can resolve activities that earlier-stage sources contributed in the
  same start. The Elsa 3 activity source MUST declare a later stage than built-in activity sources. The owning
  `EXTENSION_POINTS.md` MUST document the stage.

**Reuse and placement**

- **FR-016**: Reusable-workflow detection, reference resolution and cycle analysis MUST reuse
  `ReusableActivityCollectionAnalyzer`, and graph activity and wrapper construction MUST reuse the collection
  import's building logic. Both MUST move into a shared package with no web or EF dependencies, consumed by both
  the collection import and this feature. The reference id scheme MUST be a parameter of that shared code, so the
  collection import keeps `ReusableActivityImportIdentity` and this feature uses `ActivityCatalogStableIds`.
- **FR-017**: The collection import's observable behaviour (endpoints, persisted rows, ids, versions) MUST NOT
  change, apart from FR-010.

**Unconvertible input and diagnostics**

- **FR-018**: An Elsa 3 workflow that cannot be converted MUST be skipped together with every workflow and
  reusable workflow that depends on it, transitively. Causes include: no Elsa 4 counterpart, activity type or
  version missing from the catalog, unsupported construct, analyzer error (cycle, missing or ambiguous reference),
  duplicate input, credential literal, and instance state.
- **FR-019**: Each skipped item MUST be logged as one structured warning carrying a stable diagnostic code, the
  source file, the Elsa 3 definition id and version, and the cause; a dependent's cause MUST name the item it
  depends on. Each start MUST log one summary line with converted-activity, converted-workflow and skipped counts.
- **FR-020**: An option (for example `FailOnUnconvertible`, default `false`) MUST make any unconvertible item fail
  shell activation, with an error listing every unconvertible item.

**Coexistence, removal, tenant, boundary**

- **FR-021**: Shell activation MUST fail with a composition error when `Elsa3JsonReconciliation` and the collection
  import feature (`Elsa3ImportJsonActivities`) are composed in the same shell.
- **FR-022**: A reusable workflow whose activity type key already belongs to an activity definition authored by
  the collection import MUST be skipped with a diagnostic (FR-018/FR-019) and MUST NOT be appended to that definition.
- **FR-023**: Removing a file MUST NOT delete, retire or unpublish anything; the feature documentation MUST say so.
- **FR-024**: The feature MUST NOT add a tenant option; reconciled rows take the shell's ambient tenant scope like
  every other reconciliation source.
- **FR-025**: Elsa 3 workflow instances stay out of scope; instance state MUST be refused through the existing
  migration boundary.

### Key Entities

- **Elsa 3 input item**: one Elsa 3 workflow definition read from a configured file; identified by Elsa 3
  definition id and version, and remembers its source file.
- **Conversion plan**: the per-start result of reading and analyzing all input items: which items are reusable,
  their dependencies, the reference rewrites, and the skipped items with their diagnostics. Shared by both sources.
- **Graph activity entry**: the activity reconciliation entry for a reusable workflow (type key, `N.R.0`, graph
  manifest, inputs and outputs).
- **Workflow entry**: the workflow reconciliation entry for a plain workflow or a wrapper (Elsa 3 definition id,
  `N.R.0`, converted state).
- **Converter revision (R)**: one constant owned by the converter, recorded with the golden output.
- **Conversion diagnostic**: code, source file, definition id, version, cause, and (for dependents) the blocking item.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host started on an empty store with the four-workflow fixture folder (plain, reusable, nested
  reusable, user of reusable) has all four converted, every reference resolving, and the plain and wrapper workflows
  running to completion, with no manual step between process start and the first run.
- **SC-002**: A second start with the same input and revision adds or changes zero activity versions, workflow
  versions and publications.
- **SC-003**: A start after a revision bump adds exactly one new version per converted item and fails zero times.
- **SC-004**: A converter change that alters fixture output without a revision bump is caught by the test suite
  100% of the time.
- **SC-005**: With one unconvertible item in the folder, 100% of the remaining independent items are reconciled,
  and the skipped item and each of its dependents appear in the log with code and file.
- **SC-006**: Every existing collection-import test passes unchanged, except tests updated for the wrapper
  direct-start fix.

## Dependencies

- **Converter completeness is out of scope and tracked as separate prerequisite specs (#2705 Flowchart connections, #2706 type/member map and value-flow lowering).** Main's converter throws on
  Flowchart connections, has no Elsa 3 → Elsa 4 type and member map (parity report §2.8 renames, §4 Flow
  activities), and the collection path skips value-flow lowering. Until those land, most real Elsa 3 workflows,
  and likely most of the JTest suite, are reported as unconvertible by this feature (FR-018). The acceptance
  fixtures use constructs the converter supports today.
- Activities without an Elsa 4 counterpart (`docs/reports/elsa-4-activity-contract-parity-2026-07.md` §3:
  `StartAt`, `StateMachine`, `ParallelForEach`, `RunTask`, `BulkDispatchWorkflows`, `DownloadHttpFile`,
  `WriteFileHttpResponse`) stay unconvertible.
- Whether graph activities need `WorkflowsPublishingApi` (`IActivitySourceVersionPublisher`) to be executable
  inside a published workflow is settled in the plan; FR-005 holds either way.
- PR #2700 edits the same `EXTENSION_POINTS.md` that FR-015 updates.

## Assumptions

- The JTest suite collection is external (JTEST-nexxbiz); this spec's tests use in-repo fixtures.
- The JTest host runs on a fresh or ephemeral store; persistent-store behaviour is covered by User Stories 2 and 3.
- §E2.8 activity versioning and Model X are provisional (not covered by the 2026-08-08 ratification). This spec
  relies on their current behaviour (versions never deleted, hash mismatch throws) and must be revisited if they
  change.
- Converted workflows are source-owned (`IsSourceOwned`), with whatever Studio editing restrictions that implies today.
- The diagnostic codes reuse the existing `ELS3-…` and `VF-IMP-…` codes where they apply; new codes are added only
  for causes that have none (duplicate input, blocked by dependency, owned by collection import).

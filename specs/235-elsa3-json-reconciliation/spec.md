# Feature Specification: Elsa3JsonReconciliation — import Elsa 3 workflow JSON from a folder at engine start

**Feature Branch**: `235-elsa3-json-reconciliation`

**Created**: 2026-10-13 · **Revised**: 2026-10-30 (see *Revision 2026-10-30*)

**Status**: Draft

**Tracking**: ADO 47607, #2707; part of epic #3052 (prerequisites #2705 and #2706 are done: specs 239 and 236)

**Input**: User description: "Elsa 3 workflow definitions (JSON) must convert to Elsa 4 automatically at engine
start, with no HTTP import and no Studio step. We use this to run one engine-neutral JTest suite collection against
Elsa 3 and then, unchanged, against Elsa 4 with the converted workflows. So the conversion runs on every start, and
every suite run also tests the converter."

## Revision 2026-10-30

The first draft (2026-10-13) planned two reconciliation sources: reusable workflows as entries of an
`IActivityReconciliationSource` in a staged activity pass, and workflows through an `IWorkflowReconciliationSource`.
Since then spec 246 made a reusable workflow run as a graph activity only once it is published through the Design
draft path (`IActivityDefinitionPublisher`), which compiles the graph manifest into a template. A graph manifest
returned by an activity reconciliation source would sit in the catalog without running as a graph. The import also
already orders reusables by dependency, converts callers against their targets' published outcomes, and publishes
dependencies first.

Decisions (Joey Barten, 2026-10-30):

- **One conversion engine, two triggers.** At engine start this feature runs the existing collection import
  (`IReusableActivityCollectionImporter`) in-process on the configured files, then publishes the imported
  workflows. The HTTP collection import and this feature share the analyzer, converter, materializer, commit and
  publish phase. No core reconciler change.
- **Deferred:** the converter revision `N.R.0` and its golden-output test (the old User Story 3). Versions are the
  import's `{n}.0.0`. A converter change under the same Elsa 3 version is refused by the existing identity checks.
- **Dropped:** the guard against composing the collection import in the same shell (old FR-021). Both use one engine.
- **Kept:** skip-and-report with a strict opt-in (User Story 3 below).

## Context (verified against origin/main 4df0c5e9d, 2026-10-30; line numbers drift)

- **Startup order.** `TaskManager` sorts every `IStartupTask` of a shell by `[Order]`, then by
  `[TaskDependency]`. Activity reconciliation is `[Order(1)]` (`ActivityVersionReconcilerStartupTask`), workflow
  reconciliation `[Order(2)]`, `GitWorkflowExportStartupTask` `[Order(3)]`, `CompleteInterruptedPublicationsStartupTask`
  `[Order(5)]`. After Order 1, the catalog holds the host's built-in activities.
- **Conversion needs the catalog.** `Elsa3ActivityResolver` resolves each node through `IActivityDefinitionLookup`
  (catalog rows, highest SemVer), the built-in map (spec 236) and replaced-type aliases. The wrapper reads the
  catalog's `Sequence`. Flowchart connections (spec 239) and value-flow lowering convert on every path.
- **The import.** `ReusableActivityCollectionImporter.ApplyAsync` re-analyzes the collection, refuses a selection
  that has blocking diagnostics or is not dependency-closed, materializes (one `ReusableActivityImportValidationException`
  lists every conversion failure, located by source version), admits nodes through the credential-literal rule
  (all or nothing), commits atomically, then runs `ReusableActivityImportPublishPhase` for the reusables in
  dependency order. `AccessScope` is optional: without one, no receipt is written and the commit is a no-op when
  every row already exists with the same content.
- **What the import writes.** Reusables: activity definitions authored under `elsa3.collection-import`, published as
  graph activities (spec 246) at `{n}.0.0`. Workflows and one wrapper per reusable: workflow definition versions
  keeping the Elsa 3 definition id, **unpublished**. Nothing in `Extensions/Elsa3` publishes a workflow.
- **Tenant.** The EF commit writes only inside an explicit tenant persistence scope
  (`EfReusableActivityImportCommand.WritableTenant`). Background work in the import visits every persistence scope
  through `IPersistenceScopeRunner` (`ExpiredImportCollectionSweepTask`).
- **Publishing.** `PublishWorkflow` (`Elsa.Workflows.Publishing.Core`) publishes a workflow version.
  `PublishReconciledWorkflowVersions` skips a version that already holds the active publication of its target slot.
  `Elsa3ImportActivitiesPublishing` (DependsOn `Elsa3ImportJsonActivities`, `WorkflowsPublishingApi`) supplies the
  reusable publisher.
- **Publishing authorization.** `ActivityDefinitionPublisher` checks each Design row's tenant against
  `IActivityPublishingAuthorizationContext`, whose only implementation reads the HTTP user's tenant claim. Outside a
  request it grants only rows without a tenant, so the import's reusable publication works today only from an HTTP
  request.
- **Instance state.** The import never reads Elsa 3 instance state; a document without a root activity is refused by
  the converter.
- **Elsa 4 JSON reconciliation** (`JsonWorkflowReconciliation`) fixes the configuration shape this feature copies:
  `SourceId` plus exactly one of `FilePath`, `Files`, `FolderPath`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An Elsa 3 folder becomes runnable Elsa 4 workflows at start (Priority: P1)

A test engineer points an Elsa 4 host at a folder of Elsa 3 workflow JSON and starts it. Without any import call or
Studio step, every convertible Elsa 3 workflow is present in Elsa 4: reusable workflows as published graph activities,
all other workflows plus a wrapper per reusable workflow as published, runnable workflow definitions that keep their
Elsa 3 definition ids.

**Why this priority**: This is the whole purpose: running the same engine-neutral JTest suite against Elsa 3 and,
unchanged, against Elsa 4.

**Independent Test**: Compose a host with `Elsa3JsonReconciliation` on an empty store, configured with a folder
holding a plain workflow, a reusable workflow with an input and two outcomes, a reusable workflow that uses another
reusable workflow, and a workflow that uses the reusable one and routes on its outcomes. Start it and assert the
activities and workflows exist and are published, and that the plain workflow and the caller run to completion when
started by their Elsa 3 definition ids, the caller taking the route its input selects.

**Acceptance Scenarios**:

1. **Given** an empty store and that folder, **When** the host starts, **Then** each reusable workflow exists as a
   published graph activity version `{n}.0.0`, and every other workflow and every wrapper exists as a workflow
   definition version `{n}.0.0` with an active publication.
2. **Given** the started host, **When** the caller is started by its Elsa 3 definition id, **Then** it runs the
   reusable workflow's graph, including the nested reusable workflow, and completes on the outcome's route.
3. **Given** files in any order, **When** the host starts, **Then** reusable workflows that reference each other are
   imported and published in one start, dependencies first.
4. **Given** a workflow with an HTTP Endpoint start trigger, **When** the host is ready, **Then** its trigger is
   registered, so a request to the endpoint starts it.

---

### User Story 2 - Restarting with the same input changes nothing (Priority: P1)

The suite host restarts often. A second start with the same Elsa 3 input must not create, change, republish or fail
anything.

**Why this priority**: The import runs on every start; a non-idempotent start breaks persistent hosts.

**Independent Test**: Start twice against the same persistent store and folder; compare activity versions, workflow
versions and publications.

**Acceptance Scenarios**:

1. **Given** a store imported from a folder, **When** the host starts again with the same folder, **Then** no activity
   version, workflow version or publication is added or changed, and startup succeeds.
2. **Given** a store imported from a folder, **When** a new Elsa 3 version of one workflow is added to the folder and
   the host restarts, **Then** that version is imported and published, and nothing else changes.
3. **Given** a start that stopped after the commit but before every publication, **When** the host starts again,
   **Then** each reusable still unpublished, and the newest committed version of each workflow that was never
   published, is published.

---

### User Story 3 - Unconvertible input is skipped and reported, not fatal by default (Priority: P2)

Some Elsa 3 workflows use activities with no Elsa 4 counterpart, types missing from the catalog, or unsupported
constructs. The host still starts, skips those workflows and everything that depends on them, and reports each one
with a code, the file and the cause. A strict option makes the same condition fail startup instead.

**Why this priority**: For the suite, a skipped workflow surfaces as a failing test, which tells you more than a host
that won't start; the strict option serves hosts that must not run with gaps.

**Independent Test**: Add a reusable workflow using an Elsa 3 activity without counterpart and a workflow that uses
it; start, and assert both are absent, both are logged with codes, the summary line reports two skipped items, and the
rest of the folder is imported and published. Repeat with the strict option and assert startup fails.

**Acceptance Scenarios**:

1. **Given** an unconvertible reusable workflow X and a workflow Y that uses X, **When** the host starts, **Then**
   neither X, X's wrapper nor Y is imported; each is logged as a structured warning naming code, source file,
   definition id, version and cause (Y's cause names X); all other items are imported and published.
2. **Given** any start, **When** the import finishes, **Then** one summary log line reports the counts of imported
   activities, imported workflows, published workflows and skipped items.
3. **Given** a reusable that its publisher refuses, **When** the host starts, **Then** the workflows that use it
   are not published and are reported with the reusable as their cause; the rest is published.
4. **Given** the strict option is enabled and an item is unconvertible, **When** the host starts, **Then** startup
   fails with an error listing the skipped items, and nothing has been committed in the persistence scope that found
   them (a scope the task ran earlier may already have committed, FR-015).

---

### Edge Cases

- **Dependency cycle** between reusable workflows: every workflow in the cycle and its dependents are skipped with
  the analyzer's cycle diagnostic.
- **Missing reference** to a reusable workflow not in the configured input: the referencing workflow is skipped with
  the analyzer's diagnostic.
- **Same definition id and version in two files**: both items are skipped with a duplicate-input diagnostic.
- **Startup fails** (strict mode, or a refusal not attributed to an item): the shell does not become ready, and
  later startup, background and recurring tasks do not run.
- **Several Elsa 3 versions of one definition**: each is imported; the newest is published.
- **Credential literals** in a converted node: refused by the collection import's admission rule. The refusal does
  not say which item it came from, so it fails the start with the rule's findings (FR-014).
- **A document that is not a workflow definition** (for example exported instance state): it has no root activity,
  so the converter refuses it and it is skipped like any conversion failure.
- **File removed** from the folder: nothing is removed, retired or unpublished; documented.
- **Configured folder missing, or a file not valid JSON**: startup fails with a configuration error naming the path;
  an empty folder is logged and is not an error (mirrors `JsonWorkflowReconciliation`).
- **A changed workflow under an Elsa 3 version already imported**: refused by the import's identity checks. The
  refusal does not say which item it came from, so it fails the start; the stored version stays (FR-014).
- **A workflow an operator published another version of** (in Studio, or a rollback to an older imported version):
  a later start does not publish the imported version over it again. Only a new Elsa 3 version supersedes it.
- **A re-export that replaces a file with a newer version**: the newer version is imported and published; the older
  one stays as a retired version.
- **A reusable whose publication failed** on an earlier start: the next start tries again.
- **The same source version id twice, or a blank one**: skipped with a duplicate-input diagnostic.
- **Collection import composed in the same shell**: allowed; both use the same engine and identities, so an item
  imported by either is a no-op for the other.
- **Several nodes starting at once**: each node runs the import; the commit is idempotent and retried on a write
  conflict, so they converge on the rows one node writes. A workflow publication another node made first is
  recognised and skipped (research R-6).

## Requirements *(mandatory)*

### Functional Requirements

**Feature and configuration**

- **FR-001**: The Elsa3 extension MUST provide one shell feature, `Elsa3JsonReconciliation`, that imports the
  configured Elsa 3 input at engine start through a startup task and needs no HTTP endpoint or Studio step.
- **FR-002**: The feature MUST be configured with exactly one of `FilePath`, `Files` (ordered list) or `FolderPath`
  (top-level `*.json`, ordinal file-name order), plus a required `SourceId`, validated at registration as
  `JsonWorkflowReconciliation` does. `SourceId` names the input in logs.
- **FR-003**: Each file MUST hold either one Elsa 3 workflow definition or an array of them. The upload's size and
  count limits do not apply: the files are host configuration, not a request.
- **FR-004**: The startup task MUST run after the activity and workflow reconciliation tasks of the same start, so
  conversion resolves the host's built-in activities from the catalog.
- **FR-005**: The feature MUST declare its dependencies on `Elsa3ImportActivitiesPublishing` and
  `WorkflowsPublishing`. Composing it with its declared dependencies and the import's persistence provider (today
  `Elsa3ImportActivitiesEntityFrameworkCore`) MUST be sufficient for the imported activities and workflows to be
  published and runnable.
- **FR-006**: The task MUST import in every persistence scope the host supplies, as the import's other background
  work does; in a single-tenant host that is one scope.

**Conversion, identity and publication**

- **FR-007**: The task MUST import through the collection import (`IReusableActivityCollectionImporter`): the same
  analysis, conversion, admission, identities, versions (`{n}.0.0`) and reusable publication (spec 246) as the HTTP
  import.
- **FR-008**: After the commit, the task MUST publish the newest version in the input of each imported workflow and
  wrapper with `PublishWorkflow`, after the reusables it uses are published. Each version MUST be published at most
  once: a version its target slot has already published (active, retired or in flight) is skipped, so a later start
  never takes back a slot an operator moved. A newer Elsa 3 version, never published, is published. Each publication
  MUST carry the persistence scope's tenant, and a publication another node made first MUST be treated as done.
- **FR-008a**: The task MUST publish the reusables within the persistence scope's tenant without widening what any
  other caller of the activity publisher may access.
- **FR-009**: A workflow whose reusable dependency was not published MUST NOT be published, and MUST be reported
  with that dependency as its cause.
- **FR-010**: The collection import's observable behaviour (endpoints, persisted rows, ids, versions, receipts) MUST
  NOT change. Additive members on its in-process request and result types are allowed.

**Restart**

- **FR-011**: A start with input that is already imported and published MUST write nothing and publish nothing.
- **FR-012**: A start MUST publish what an earlier start committed but did not publish: every reusable still
  unpublished, including one whose earlier publication was refused or failed, and, per workflow, the newest committed
  version if it was never published (FR-008). Older committed workflow versions stay unpublished.

**Unconvertible input and diagnostics**

- **FR-013**: An Elsa 3 workflow that cannot be imported MUST be skipped together with every workflow and reusable
  workflow that depends on it, transitively. Causes include: blocking analysis diagnostics (cycle, missing reference,
  start trigger or intrinsic in a reusable), conversion failure (including a document that is not a workflow
  definition), duplicate input and a reusable that was not published.
- **FR-014**: Each skipped item MUST be logged as one structured warning carrying a stable diagnostic code, the
  source file, the Elsa 3 definition id and version, and the cause; a dependent's cause MUST name the item it depends
  on. Conversion warnings (`ELS3-BEHAVIOUR-*`) of imported items MUST be logged too. Each start MUST log one summary
  line per persistence scope with imported-activity, imported-workflow, published-workflow and skipped counts. A
  refusal the import does not attribute to an item (a credential literal, an identity collision) MUST fail the start
  with the import's message and guidance; nothing is committed in that scope.
- **FR-015**: An option `FailOnUnconvertible` (default `false`) MUST make any skipped item fail startup with an error
  listing the skipped items. An item skipped before the commit MUST fail startup before anything is committed in that
  persistence scope; a publication refused after the commit fails startup after it.

**Removal and boundary**

- **FR-016**: Removing a file MUST NOT delete, retire or unpublish anything; the feature documentation MUST say so.
- **FR-017**: The feature MUST NOT add a tenant option.
- **FR-018**: Elsa 3 workflow instances stay out of scope: a document that is not a workflow definition MUST be
  skipped (FR-013), never imported.

### Key Entities

- **Elsa 3 input item**: one Elsa 3 workflow definition read from a configured file; identified by Elsa 3 definition
  id and version, and remembers its source file.
- **Import selection**: the items of one start that can be imported: no blocking diagnostic, no failed dependency.
- **Skipped item**: an input item left out, with its code, file, definition id, version and cause.
- **Import outcome**: per persistence scope, the imported activities and workflows, the publications and the skipped
  items of one start.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host started on an empty store with the fixture folder (plain, reusable, nested reusable, caller) has
  all of them imported and published, and the plain workflow and the caller run to completion, with no manual step
  between process start and the first run.
- **SC-002**: A second start with the same input adds or changes zero activity versions, workflow versions and
  publications.
- **SC-003**: With one unconvertible item in the folder, 100% of the remaining independent items are imported and
  published, and the skipped item and each of its dependents appear in the log with code and file.
- **SC-004**: Every existing collection-import test passes unchanged.

## Dependencies

- The converter prerequisites #2705 (Flowchart connections, spec 239) and #2706 (activity map, spec 236) are done.
  Converter completeness stays out of scope: what the converter refuses is reported as skipped (FR-013).
- Activities without an Elsa 4 counterpart (`docs/reports/elsa-4-activity-contract-parity-2026-07.md` §3) stay
  unconvertible.
- A reusable workflow cannot return output values until #3023 (S-7 on epic #3052); a reusable that sets an output is
  skipped with the import's existing diagnostic.

## Assumptions

- The JTest suite collection is external (JTEST-nexxbiz); this spec's tests use in-repo fixtures.
- Elsa 4 is not in production, so no stored Elsa 4 data has to survive a converter or serialization change. After
  such a change (the converter specs S-0 to S-8 and S-9 on epic #3052), a host that imported earlier rebuilds its
  store; the README says so. The converter revision is the follow-up before GA (research R-10).
- §E2.8 activity versioning and Model X are provisional. This feature writes through the import's commit, not through
  the reconcilers, so it relies on the import's identity rules rather than on Model X.
- Imported workflows are not source-owned (the import does not set `IsSourceOwned`), so a reconciliation source's
  soft delete never applies to them; FR-016 holds.
- The diagnostic codes reuse the existing `ELS3-…`, `VF-IMP-…` and import codes; new codes are added only for causes
  that have none (duplicate input, blocked by dependency).

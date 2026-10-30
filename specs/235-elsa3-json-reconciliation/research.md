# Research: Elsa3JsonReconciliation (spec 235)

Verified against origin/main 4df0c5e9d on 2026-10-30, and reviewed against the code the same day (an independent
review found two blockers in the first version of this file; R-3 and R-5 carry the corrections). Line numbers drift.

## R-1 Mechanism: run the collection import at start, not reconciliation sources

- **Decision**: A startup task runs `IReusableActivityCollectionImporter` in-process on the configured files, then
  publishes the imported workflows. No `IActivityReconciliationSource` or `IWorkflowReconciliationSource`.
- **Rationale**:
  - A reusable runs as a graph activity only once `IActivityDefinitionPublisher` publishes its Design draft and
    `GraphActivityProvider.CompileAsync` turns the manifest into a template (`WorkflowExecutableCompiler.cs:154-187`).
    Its catalog row exists only after that publication (`EfReusableActivityImportCommand.cs:195`).
  - An entry from an activity reconciliation source is compiled from the catalog row as an `AuthorableActivity`
    (`WorkflowExecutableCompiler.cs:145-152`). With `WorkflowsPublishingApi` composed, `SourceOwnedActivityVersionPublisher`
    would even publish it as a single-node root under the same version id, which the Design path then refuses.
  - The import already orders reusables by dependency (`Elsa3ReusableImportOrder`), converts callers against the
    targets' converted boundary outcomes, admits nodes through the credential-literal rule and publishes dependencies
    first (`ReusableActivityImportPublishPhase`).
- **Alternatives considered**:
  - Reconciliation sources plus a staged activity pass (the 2026-10-13 draft): needs the Design publish path anyway,
    an extraction of the materializer into a web-free package, and a core reconciler change. Two to three days, for no
    behaviour the import lacks.
  - An Elsa 4 JSON export step (convert to Elsa 4 JSON files, then `JsonWorkflowReconciliation`): needs resolved
    activity version ids offline and still cannot publish graph activities.

## R-2 When the task runs

- **Decision**: `[Order(4)]`: after activity reconciliation (1), workflow reconciliation (2) and the Git export (3),
  before `CompleteInterruptedPublicationsStartupTask` (5). Order 4 is unused (in use: -100, 1, 2, 3, 5).
- **Rationale**: conversion reads catalog rows through `IActivityDefinitionLookup`; they exist after Order 1. Order
  is the existing mechanism (`TaskManager.cs:109-116`); a `[TaskDependency]` would make this project reference the
  reconciliation implementation projects.

## R-3 Tenant, persistence scope and publishing authorization

- **Decision**:
  1. Run the import once per persistence scope through `IPersistenceScopeRunner.RunAsync`, as
     `ExpiredImportCollectionSweepTask` does, and resolve every scoped service (importer, request sender, workflow
     publisher, publication read ports) from that operation scope.
  2. `ReusableActivityImportApplyRequest` gains an additive `string? TenantId`, used only when `AccessScope` is null:
     the importer stamps it on the mutation's activities and workflows exactly as the scoped branch stamps the access
     scope's tenant (`ReusableActivityCollectionImporter.cs:140-160`). The task passes the operation scope's tenant.
  3. The feature decorates `IActivityPublishingAuthorizationContext` with `Elsa3StartupPublishingGrant`: a scoped
     decorator that answers for one tenant only while a scoped grant holder in the same DI scope is set, and delegates
     to the inner (HTTP) context otherwise. Only the startup task sets the holder, inside the operation scope the runner
     created for it, so no HTTP request ever shares that scope.
- **Rationale**:
  - `IPersistenceScopeRunner` binds `PersistenceAccessContext.Scoped(scope)` in each operation scope
    (`PersistenceServiceScopeFactoryExtensions.cs:26-27`), which is what `EfReusableActivityImportCommand.WritableTenant`
    needs.
  - Without (2) and (3), every reusable is refused at startup. The commit stamps the persisted rows with the scope's
    tenant (`"default"` in a single-tenant host), while the unscoped mutation keeps `TenantId = null`. The outer check
    in `PublisherReusableActivityImportPublisher.cs:26` passes (null equals null), but `ActivityDefinitionPublisher`
    then calls `EnsureAuthorized(row.TenantId)` (`:456`, `:463`, `:472`, `:505`, `:639-645`).
    `HttpContextActivityPublishingAuthorizationContext.CanAccessTenant("default")` is false without an `HttpContext`, so
    every reusable fails with `activity.tenant.reference-denied` and every caller is blocked. Existing import tests pass
    only because the test host's user carries a `"default"` tenant claim (`Elsa3ImportHost.cs:130`).
  - The context is registered shell-wide with `TryAddScoped` (`WorkflowsPublishingApiFeature.cs:56`), so a per-scope
    replacement is impossible; a decorator that delegates by default leaves every other caller unchanged.
- **Alternatives considered**:
  - An `AsyncLocal` grant: works, but ambient flow across awaits is harder to reason about than a value held by the
    DI scope.
  - Replacing the context shell-wide with "the ambient persistence tenant when there is no HTTP context": widens what
    every background caller of the publisher may access.

## R-4 Unscoped apply

- **Decision**: Apply with `AccessScope = null`, no idempotency key, `TenantId` from R-3, and an additive
  `OperationKey` that is new on every start (`elsa3-reconcile:{SourceId}:{start id}`).
- **Rationale**: a scoped apply needs a current user (`Elsa3ImportScopeGuard.EnsureCurrent`) and writes a receipt per
  user. Unscoped, `Validate` needs neither (`ReusableActivityImportCommitRules.cs:17-41`), and the commit writes nothing
  when every row exists with the same content (`EfReusableActivityImportCommand.cs:144`; timestamps are deterministic,
  materializer `:172-217`).
- **Why a per-start operation key**: without a receipt the publish phase keys each publication
  `elsa3-import:{planId}:{versionId}` (importer `:166`, phase `:104`), and the plan id depends only on the input
  (analyzer `ComputePlanId`). `ActivityDefinitionPublisher.PublishReviewedAsync` stores a terminal receipt under that key
  (`:208-249`, `:985-1008`, EF-persisted) and replays it on the next start (`:193-201`, `:947-971`). So a reusable refused
  once, for a transient or since-fixed cause, would stay refused, with all its callers, until the input changed. A new
  key per start avoids that. Re-publishing is safe: the phase checks for an existing publication first (`:74-81`), so a
  version already published is reported `AlreadyPublished`.
- **Consequence**: `ApplyAsync` drops the publication states when there is no receipt (importer `:167`). The apply
  result gains two additive members (data-model.md): the publication states and the committed workflow versions. The
  HTTP apply returns the receipt, not the apply result, so nothing leaks to the endpoints (FR-010).

## R-5 Choosing what to apply

- **Decision**: Narrow the selection in three steps. Each removes the failing items and, transitively, everything that
  depends on them (through the plan items' `Dependencies`).
  1. **Input**: a blank source version `Id`; the same source version `Id` more than once; the same
     `(definition id, version)` more than once. The analyzer drops duplicate and blank ids with a plan-level diagnostic
     that names no item (`ReusableActivityCollectionAnalyzer.cs:27-37`), so they must be caught here to be reported.
  2. **Analysis**: items with an error diagnostic in the plan (cycle, missing reference, trigger or intrinsic in a
     reusable, unsupported shape, and `InvalidSource` for a document with no root, analyzer `:73-81`). A plan-level
     error diagnostic that names no item fails the start.
  3. **Apply**: when `ApplyAsync` throws `ReusableActivityImportValidationException`, locate each error diagnostic's
     item by its `metadata["SourceVersionId"]` (converter diagnostics, `Elsa3ConversionDiagnostics.cs:67-72`, and the
     mapping failure, `Elsa3DefinitionConverter.cs:247-256`), falling back to a `SourceVersion` path segment (cycle
     diagnostic, materializer `:70`). Remove the located items and their dependents and apply again. Each round removes
     at least one item, so it ends.
- **Not locatable, so fatal for the scope** (the start fails with an error naming the cause and its guidance):
  - `CredentialLiteralRefusedException`: its findings carry the location only in message text
    (`CredentialLiteralFinding.cs:23-24`), which is not a sound basis for skipping.
  - Identity collision (a changed workflow under an Elsa 3 version already imported): the unscoped commit throws a
    plain `InvalidOperationException` from preflight (`EfReusableActivityImportCommand.cs:207-279`, `:943-944`).
  - Any other `InvalidOperationException` from the apply (for example no `Sequence` in the catalog, materializer
    `:284`, or a rewrite mismatch, `:140`, `:146`), and a validation error with no source version. These are reported
    as an unattributed refusal with the original message, never as a collision.
  Making these skippable needs a located diagnostic in the import; that is a follow-up, not this spec.
- **A changed reusable whose earlier publication failed**: no catalog row exists yet, so the commit accepts it and
  the publish phase returns `Refused` with its identity-collision code instead of throwing (phase `:71-72`,
  `:151-157`). It is skipped with that code, and its callers are blocked (FR-009).
- **Not a workflow definition** (for example an exported instance): the import never calls
  `Elsa3MigrationBoundary.RejectLiveInstanceResume`. Such a document has no root, so the analyzer reports it as
  `InvalidSource` and it is skipped in step 2.
- **Strict mode** (FR-015): a skip in step 1 or 2 fails the scope before any apply; the first refused apply in step 3
  fails it before its commit; a refused publication fails it after the publish step. The runner visits every scope and
  throws an `AggregateException` when several fail (`PersistenceScopeRunner.cs:50-53`), so "nothing committed" holds per
  scope.
- **What a failed start means**: `TaskManager` logs and rethrows (`:70-74`), and the shell's activation aborts, so the
  shell never becomes Active: later startup tasks (Order 5), background and recurring tasks do not run, and
  `/health/ready` stays unready.

## R-6 Publishing the imported workflows

- **Decision**: **Each imported version is published at most once.** For each workflow definition in the input,
  take its newest version in the input (T) and:
  - skip it when a reusable it depends on is not `Published` or `AlreadyPublished` (FR-009);
  - resolve T's target slot; skip it when the slot's publication journal (`IPublicationRecordStore.ListBySlotAsync`)
    already holds a record of T in any status but `Failed` (active, retired, or in flight, which
    `CompleteInterruptedPublicationsStartupTask` finishes at Order 5);
  - otherwise send `IRequestSender.Send(new PublishWorkflow(T))`.
  Workflows publish after the import's own reusable publish phase.
- **Why the journal, not the input**: the input says nothing about what the operator did since.
  - A re-export that replaces v1 with v2 in the same file: v2 was never published, so it is published. Deciding by
    "is the active version in this start's input" would treat v1 as an operator's version and never publish v2.
  - An operator rolls back to an older imported version, or publishes a Studio version: T was already published once,
    so the import leaves the slot alone (FR-011, and the contract's "never taken back").
  - A new Elsa 3 version arrives later: it was never published, so it is published over whatever is active. A new
    source version supersedes; the README says so.
  - A commit whose publish step was interrupted: T was never published, so the next start publishes it (FR-012).
  - `ListBySlotAsync` is implemented by the EF store; `ListByDefinitionIdsAsync` is not (`EfPublicationRecordStore.cs:24`).
- **Rationale for the request**: the same request `PublishReconciledWorkflowVersions` sends (`:110`), with the same
  slot resolution. Its `ResolveTargetSlotName` and the slot lookup of `HasActivePublicationOf` (policy store, policy
  resolver, activation authority; `:121-160`) are copied and adapted to return the slot, about thirty lines, rather
  than shared (framework §2.17), because they are private to `Elsa.Workflows.Publishing`.
- **Alternatives considered**: always send `PublishWorkflow`. It short-circuits an unchanged active artifact
  (`PublishWorkflowRequestHandler.cs:135-145`), but it saves the executable first (`:131`), so a restart would still
  write; and it would take back a slot an operator moved.

## R-7 Where the code lives

- **Decision**: A new project `Extensions/Elsa3/src/Activities/Design/Import/Reconciliation/Elsa3.Activities.Design.Import.Reconciliation.csproj`.
- **Rationale**: it needs the import, `Elsa.Workflows.Publishing.Core` (`PublishWorkflow`, publication read ports),
  `Elsa.Workflows.Publishing.Api` (`IActivityPublishingAuthorizationContext`, R-3), `Elsa.Workflows.Runtime.Core`
  (`IPersistenceScopeRunner`, `IWorkflowActivationAuthority`), `Elsa.Tasks.Core` and `Elsa.Mediator.Core`. The import
  project itself must stay free of Publishing (its csproj comment and framework §2.1). §E2.7 allows support beside
  `Elsa3.Activities.Design.Import` under the same adapter name.
- **Tests**: `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests` already composes hosts that import,
  publish and run reusables (spec 246). It gains a reference to the new project; no new test project, so the CI tier
  manifest is unchanged.

## R-8 Versions

- **Decision**: The import's `{n}.0.0`. The converter revision `N.R.0` and the golden-output test are deferred
  (spec revision 2026-10-30).
- **Consequence**: after a converter change, a restart against a persistent store hits the import's identity collision
  for changed items and fails the start (R-5). Before Elsa 4 GA that store is rebuilt (R-10). JTest hosts run on fresh
  stores. The converter revision `N.R.0` is what lets stored data survive later.

## R-9 Names and §E2.7

- **Names**: the feature id stays `Elsa3JsonReconciliation` (the spec's and issue's name, and what operators look for
  next to `JsonWorkflowReconciliation`). Its types say what it does, an import: `Elsa3FolderImportTask` (R1: four
  components), `Elsa3JsonFileReader`, `Elsa3ImportSelection`, `ImportedWorkflowPublisher`, `Elsa3StartupPublishingGrant`.
- **§E2.7 "one-way, one-time import"**: each Elsa 3 version is imported once; a later start finds it and writes
  nothing (R-4). Running the check on every start is how a folder deployment stays declarative, as Model X does for
  reconciliation. Nothing is translated back, and Elsa 3 never runs.

## R-10 Alignment with epic #3052 (checked 2026-10-30)

Spec 235 checked against every decision (D-1 to D-13), analysis (A-1 to A-5), ADR draft (#3384, #3388, #3389, #3390,
#3286, #3420, #3425, #3422, #3408, #3411, #3412) and planned spec (S-0 to S-11) on the board, and against the
initiative process draft (#3413).

**No conflict.** The goal statement names this spec as the second entry point of `IElsa3DefinitionConverter`.
Running the import engine keeps one converter, so every converter decision applies to the folder path
automatically: D-1/D-5 (time and GUID functions), D-4 (explicit outcomes), D-7 (`variables.X`), D-10/D-11 (null,
undefined policy), D-12, D-13 (faithful or refuse), A-1 (`Object` retired, the date rule and its source time zone).
Options those decisions put on the converter feature (D-11's undefined policy, the source time zone) are read from
`Elsa3Mapping` by both paths; this feature adds none. ADR 0090/#3425 and S-7 lift #3023 for both paths at once.
ADR 0092, ADR 0093, ADR 0048 (D-8, D-9) and the D-2 details are runtime semantics; this feature does not touch them.

**Crooked once the board's specs land:**

1. **Every converter change breaks a persistent host's restart.** S-0, S-2, S-3, S-4, S-7 and S-8 each change what
   the converter emits for the same Elsa 3 input. With versions fixed at `{n}.0.0` (R-8), the next start re-converts,
   gets different content under the same identity, and the import's collision check (`SameBinding`,
   `JsonNode.DeepEquals` over the binding material, `EfReusableActivityImportCommand.cs:199-208`, `:525-542`) fails the
   start (R-5). Deferring the converter revision was safe for one spec; with six converter-changing specs queued it is
   not. The HTTP import has the same problem for re-imports.
2. **S-9 (ADR 0091) can do the same without any converter change.** Keeping explicit nulls (P-8), raw `JsonElement`
   passthrough and one encoder change serialized state text, and the import compares serialized material. S-9 intake
   item 7 lists the Git canonical hash and the design-store fingerprints, but not the Elsa 3 import's binding material.
3. **Approval route.** Under #3413, a spec in an initiative is approved when every approver in its approval plan
   approves its PR, and production implementation needs the approved spec. Code Review alone does not approve it. The
   spec also needs a row on the #3052 board. It depends on no pending ADR, so it can be approved now rather than
   staying provisional.

**Not on the board, so for architect review rather than an ADR:** the startup publishing grant (R-3) changes who the
activity publisher accepts outside a request. No ADR covers publishing authorization today; the PR flags it.

**Decision on items 1 and 2** (Joey Barten, 2026-10-30): Elsa 4 is not in production, so no stored Elsa 4 data has
to survive a converter or serialization change. Keep `{n}.0.0`. After a converter upgrade, or S-9, a host that
imported with an earlier converter rebuilds its store; the feature README says so. The converter revision
(`{n}.{R}.0` with the golden test) is the follow-up for when stored data must survive, before Elsa 4 GA. Rejected for
now: un-deferring it in this spec, which adds work that protects data nobody keeps yet.

# Research: Elsa3JsonReconciliation (spec 235)

Verified against origin/main 4df0c5e9d on 2026-10-30. Line numbers drift.

## R-1 Mechanism: run the collection import at start, not reconciliation sources

- **Decision**: A startup task runs `IReusableActivityCollectionImporter` in-process on the configured files, then
  publishes the imported workflows. No `IActivityReconciliationSource` or `IWorkflowReconciliationSource`.
- **Rationale**:
  - A reusable runs as a graph activity only once `IActivityDefinitionPublisher` publishes its Design draft and
    `GraphActivityProvider.CompileAsync` turns the manifest into a template (`WorkflowExecutableCompiler.cs:154-187`).
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
  before `CompleteInterruptedPublicationsStartupTask` (5).
- **Rationale**: conversion reads catalog rows through `IActivityDefinitionLookup`; they exist after Order 1. Order
  is the existing mechanism (`TaskManager.cs:109-116`); a `[TaskDependency]` would make this project reference the
  reconciliation implementation projects.
- **Alternatives considered**: Order 2 with `[TaskDependency(typeof(WorkflowsVersionReconcilerStartupTask))]`: the
  same effect with an implementation-project reference.

## R-3 Tenant and persistence scope

- **Decision**: Run the import once per persistence scope through `IPersistenceScopeRunner.RunAsync`, the way
  `ExpiredImportCollectionSweepTask` does, and resolve the importer from that operation scope.
- **Rationale**: `EfReusableActivityImportCommand.WritableTenant` refuses a write without an explicit tenant scope.
  The runner supplies one scope per tenant partition, and a single-tenant host gets one.
- **Check during implementation**: the reusable publisher compares `IActivityPublishingAuthorizationContext.TenantId`
  with the request's tenant (`PublisherReusableActivityImportPublisher.cs:26`). Outside a request the HTTP context
  gives `null`. An unscoped apply leaves `TenantId` unset on the in-memory versions, so the request carries `null` too
  and they match. A test proves the published graph activity in the scope's tenant. If it does not hold, the feature
  registers a startup authorization context for the task's scope (an additive replacement inside the task's
  operation scope only).

## R-4 Unscoped apply

- **Decision**: Apply with `AccessScope = null` and no idempotency key.
- **Rationale**: a scoped apply needs a current user and tenant (`Elsa3ImportScopeGuard.EnsureCurrent`) and writes a
  receipt per user. Unscoped, the commit writes nothing when every row exists with the same content
  (`EfReusableActivityImportCommand` preflight, `!preflight.HasWrites && receipt is null` → no-op), which is what a
  restart needs (FR-011). The publish phase is resumable on its own: it skips a version already published (FR-012).
- **Consequence**: `ApplyAsync` drops the publication states when there is no receipt
  (`ReusableActivityCollectionImporter.cs:161-168`). The apply result gains two additive members (data-model.md):
  the publication states and the committed workflow versions. The HTTP endpoints do not expose them (FR-010).

## R-5 Choosing what to apply

- **Decision**: Build the selection in three narrowing steps, each removing the failing items and, transitively,
  everything that depends on them (through the plan items' `Dependencies`):
  1. **Input**: duplicate `(definition id, version)` across files; instance-state documents (migration boundary).
  2. **Analysis**: items with an error diagnostic in the plan (cycle, missing reference, trigger or intrinsic in a
     reusable, unsupported shape).
  3. **Apply**: when `ApplyAsync` throws `ReusableActivityImportValidationException` (conversion failures, located by
     their `SourceVersion` path segment) or `CredentialLiteralRefusedException` (findings located per workflow
     version), remove the located items and their dependents and apply again. Each round removes at least one item,
     so it ends; an exception that names no item fails the start.
- **Rationale**: the apply is all or nothing (`ReusableActivityCollectionImporter` remarks). Re-applying converts the
  remaining items again, which is acceptable for a startup step: conversion is in-memory and the commit happens once.
- **Strict mode** (FR-015): a skip in step 1 or 2 fails the start before any apply. In step 3, the first refused
  apply fails the start; the apply throws before its commit, so nothing is written. A publisher refusal happens after
  the commit, so strict mode fails the start after the commit, listing the refused items.

## R-6 Publishing the imported workflows

- **Decision**: For each committed workflow definition, publish its newest imported version with
  `IRequestSender.Send(new PublishWorkflow(versionId))`, skip it when that version already holds the active
  publication of its target slot, and skip (and report) it when a reusable it depends on is not `Published` or
  `AlreadyPublished`. Workflows publish after the import's own reusable publish phase.
- **Rationale**: the same request `PublishReconciledWorkflowVersions` sends. The already-active check mirrors its
  `HasActivePublicationOf` (policy store, policy resolver, activation authority, record store); it is twenty lines,
  copied rather than shared (framework §2.17, duplication beats dependency), because the handler's version is private
  to `Elsa.Workflows.Publishing`.
- **Alternatives considered**: always send `PublishWorkflow` and treat "already published" as success: a second start
  would create a new publication record per workflow, which breaks FR-011.

## R-7 Where the code lives

- **Decision**: A new project `Extensions/Elsa3/src/Activities/Design/Import/Reconciliation/Elsa3.Activities.Design.Import.Reconciliation.csproj`.
- **Rationale**: it needs the import, `Elsa.Workflows.Publishing.Core` (`PublishWorkflow`, publication read ports),
  `Elsa.Workflows.Runtime.Core` (`IPersistenceScopeRunner`, `IWorkflowActivationAuthority`), `Elsa.Tasks.Core` and
  `Elsa.Mediator.Core`. The import project itself must stay free of Publishing (its csproj comment and framework
  §2.1); the Publishing bridge project is the reusable-publisher adapter and should not grow a startup feature.
  §E2.7 allows support beside `Elsa3.Activities.Design.Import` under the same adapter name.
- **Tests**: `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests` already composes hosts that import,
  publish and run reusables (spec 246). It gains a reference to the new project; no new test project, so the CI tier
  manifest is unchanged.

## R-8 Versions

- **Decision**: The import's `{n}.0.0`. The converter revision `N.R.0` and the golden-output test are deferred
  (spec revision 2026-10-30).
- **Consequence**: after a converter change, a restart against a persistent store refuses the changed items with the
  import's collision diagnostics and reports them as skipped; the JTest host runs on fresh stores.

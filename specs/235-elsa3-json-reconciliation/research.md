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

- **Decision**: Apply with `AccessScope = null`, no idempotency key, and `TenantId` from R-3.
- **Rationale**: a scoped apply needs a current user (`Elsa3ImportScopeGuard.EnsureCurrent`) and writes a receipt per
  user. Unscoped, `Validate` needs neither (`ReusableActivityImportCommitRules.cs:17-41`), and the commit writes nothing
  when every row exists with the same content (`EfReusableActivityImportCommand.cs:144`; timestamps are deterministic,
  materializer `:172-217`). The publish phase skips versions already published and works without a receipt (operation
  key = plan id, importer `:166`).
- **Consequence**: `ApplyAsync` drops the publication states when there is no receipt (importer `:167`). The apply
  result gains two additive members (data-model.md): the publication states and the committed workflow versions. The
  HTTP apply returns the receipt, not the apply result, so nothing leaks to the endpoints (FR-010).

## R-5 Choosing what to apply

- **Decision**: Narrow the selection in three steps. Each removes the failing items and, transitively, everything that
  depends on them (through the plan items' `Dependencies`).
  1. **Input**: duplicate `(definition id, version)` across files.
  2. **Analysis**: items with an error diagnostic in the plan (cycle, missing reference, trigger or intrinsic in a
     reusable, unsupported shape).
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
  - A validation error with no source version.
  Making these skippable needs a located diagnostic in the import; that is a follow-up, not this spec.
- **Not a workflow definition** (for example an exported instance): the import never calls
  `Elsa3MigrationBoundary.RejectLiveInstanceResume`. Such a document has no root, so the converter reports it
  (`Elsa3DefinitionConverter.cs:30-32`) and it is skipped by step 3 like any conversion failure.
- **Strict mode** (FR-015): a skip in step 1 or 2 fails the scope before any apply; the first refused apply in step 3
  fails it before its commit; a refused publication fails it after the publish step. The runner visits every scope and
  throws an `AggregateException` when several fail (`PersistenceScopeRunner.cs:49-70`), so "nothing committed" holds per
  scope.

## R-6 Publishing the imported workflows

- **Decision**: For each workflow definition the import committed, take its newest imported version and:
  - skip it when a reusable it depends on is not `Published` or `AlreadyPublished` (FR-009);
  - skip it when the target slot already holds the active publication of that version (FR-011);
  - skip it when the target slot holds an active publication of a version this import did not write (an operator
    published another version in Studio): the import must not take the slot back;
  - otherwise send `IRequestSender.Send(new PublishWorkflow(versionId))`.
  Workflows publish after the import's own reusable publish phase.
- **Rationale**: the same request `PublishReconciledWorkflowVersions` sends, with the same slot resolution. Its
  `HasActivePublicationOf` and `ResolveTargetSlotName` (policy store, policy resolver, activation authority, record
  store) are copied, about thirty lines, rather than shared (framework §2.17), because they are private to
  `Elsa.Workflows.Publishing`. That handler guards against the same hijack (`PublishReconciledWorkflowVersions.cs:94-97`).
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
  for changed items and fails the start (R-5). JTest hosts run on fresh stores. A located collision diagnostic, or
  `N.R.0`, is what makes this skippable later.

## R-9 Names and §E2.7

- **Names**: the feature id stays `Elsa3JsonReconciliation` (the spec's and issue's name, and what operators look for
  next to `JsonWorkflowReconciliation`). Its types say what it does, an import: `Elsa3FolderImportTask` (R1: four
  components), `Elsa3JsonFileReader`, `Elsa3ImportSelection`, `ImportedWorkflowPublisher`, `Elsa3StartupPublishingGrant`.
- **§E2.7 "one-way, one-time import"**: each Elsa 3 version is imported once; a later start finds it and writes
  nothing (R-4). Running the check on every start is how a folder deployment stays declarative, as Model X does for
  reconciliation. Nothing is translated back, and Elsa 3 never runs.

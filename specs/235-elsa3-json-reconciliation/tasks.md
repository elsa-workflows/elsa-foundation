# Tasks: Elsa3JsonReconciliation — import Elsa 3 workflow JSON from a folder at engine start

**Input**: Design documents from `specs/235-elsa3-json-reconciliation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/feature-configuration.md, quickstart.md

**Tests**: Required by framework §2.23: a registration test for the feature (its wiring validates options) and
behaviour tests for the selection, the workflow publisher and the startup task. The Elsa constitution declares no TDD
cadence, so tests follow the implementation within each story. All tests are xunit only.

**Paths**: `IMPORT` = `Extensions/Elsa3/src/Activities/Design/Import`;
`RECON` = `Extensions/Elsa3/src/Activities/Design/Import/Reconciliation`;
`TESTS` = `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1, US2, US3 from spec.md

---

## Phase 1: Setup

- [ ] T001 Create the project `RECON/Elsa3.Activities.Design.Import.Reconciliation.csproj`: packages `CShells.Abstractions` and `Elsa.Specifications.PackageManifest.Generator` (PrivateAssets all); project references `IMPORT/Elsa3.Activities.Design.Import`, `Essentials/src/Workflows/Publishing/Core`, `Essentials/src/Workflows/Publishing/Api` (for `IActivityPublishingAuthorizationContext`), `Essentials/src/Workflows/Runtime/Core`, `Essentials/src/Tasks/Core`, `Essentials/src/Mediator/Core`; `InternalsVisibleTo` `Elsa3.Activities.Design.Import.Integration.Tests`. Mirror the layout of `IMPORT/Publishing/Elsa3.Activities.Design.Import.Publishing.csproj`.
- [ ] T002 Add the project to `Elsa.Server.slnx` next to the import's Publishing project, and add a project reference to it from `TESTS/Elsa3.Activities.Design.Import.Integration.Tests.csproj`. Regenerate the solution filters with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` and commit them only if they change.

---

## Phase 2: Foundational (blocks every story)

- [ ] T003 Add the additive members to `IMPORT/Elsa3.Activities.Design.Import/Models/ReusableActivityImportPlan.cs`: `ReusableActivityImportApplyRequest` gains `string? TenantId = null` and `string? OperationKey = null` (appended; used only when `AccessScope` is null); `ReusableActivityImportApplyResult` gains `IReadOnlyList<ReusableActivityPublicationState>? Publications = null` and `IReadOnlyList<ReusableActivityImportedWorkflowVersion>? Workflows = null` (appended, so existing constructors compile); add `public sealed record ReusableActivityImportedWorkflowVersion(string SourceVersionId, string WorkflowDefinitionId, string WorkflowVersionId, string Version)` with an XML doc (data-model.md).
- [ ] T004 Use them in `IMPORT/Elsa3.Activities.Design.Import/Services/ReusableActivityCollectionImporter.cs`: when `AccessScope` is null and `TenantId` is set, stamp it on the same entities the scoped branch stamps (activity definition, version, authoring state; workflow definition, version) before the commit, and pass `OperationKey` (when set) to the publish phase instead of `receipt?.ReceiptId ?? mutation.PlanId` (research R-4); keep the publish phase's result even when `receipt` is null and return it as `Publications`; return `Workflows` from `mutation.Workflows` (`SourceVersionId`, `Definition.Id`, `Version.Id`, `Version.Version`). The HTTP endpoints and the receipt are unchanged (FR-010).
- [ ] T005 [P] Create `RECON/Options/Elsa3JsonReconciliationOptions.cs` (`SourceId`, `FilePath`, `Files` of `Elsa3JsonReconciliationFileOption(int Order, string FilePath)`, `FolderPath`, `FailOnUnconvertible`), mirroring `Essentials/src/Workflows/Design/Reconciliation/Json/Options/JsonWorkflowReconciliationOptions.cs` without referencing it (§2.17).
- [ ] T006 [P] Create `RECON/Exceptions/Elsa3ReconciliationFailedException.cs` (public): one domain exception for an unreadable input path (names the path), for strict mode (lists the skipped items' code, file, definition id, version and cause), and for a refusal the import does not attribute to an item (wraps it, keeps its message and guidance).
- [ ] T007 Create `RECON/Services/Elsa3JsonFileReader.cs` (internal): resolves the effective files (ordered `Files`, else `FilePath`, else top-level `*.json` of `FolderPath` in ordinal file-name order; missing folder or file throws T006 naming the path; an empty folder logs once) and reads each file as one `Elsa3WorkflowDefinition` or an array of them, with the serializer options the upload uses (`ReusableActivityImportOperationService`, around line 97). Returns `Elsa3InputItem(string FilePath, Elsa3WorkflowDefinition Definition)` records.
- [ ] T008 Create `RECON/Services/Elsa3StartupPublishingGrant.cs` (research R-3, data-model.md): an internal scoped grant holder (`Grant(string? tenantId)`, read-only afterwards) and the internal decorator of `IActivityPublishingAuthorizationContext` that answers for the granted tenant only while the holder is set, and delegates to the inner context otherwise. Add tests in `TESTS/Elsa3StartupPublishingGrantTests.cs`: without a grant every call reaches the inner context; with a grant `TenantId` is the granted tenant and `CanAccessTenant` accepts only `null` and that tenant; a second scope never sees another scope's grant.
- [ ] T009 Create `RECON/Elsa3JsonReconciliationFeature.cs`: `[ShellFeature("Elsa3JsonReconciliation", DependsOn = { "Elsa3ImportActivitiesPublishing", "WorkflowsPublishing" })]` with manifest attributes as `Elsa3ImportActivitiesPublishingFeature`; public, not sealed, `virtual ConfigureServices`; an `Options` property bound from configuration; validates at registration that `SourceId` is set and exactly one of `FilePath`, `Files`, `FolderPath` is given (throw `FeatureConfigurationException` naming the rule); registers the options, the reader, the selection, the workflow publisher, the startup task (`IStartupTask`), and the startup publishing grant: a scoped grant holder plus a decoration of the already-registered scoped `IActivityPublishingAuthorizationContext` that keeps the inner registration (T008). Register types that later tasks add (selection, workflow publisher, startup task) in those tasks.

**Checkpoint**: the project builds; `dotnet build` of `RECON` and `TESTS` succeeds.

---

## Phase 3: User Story 1 — an Elsa 3 folder becomes runnable Elsa 4 workflows at start (P1) 🎯 MVP

**Goal**: on an empty store, every convertible item in the folder is imported, reusables published, workflows and
wrappers published, and the caller runs.

**Independent test**: host with the feature on a folder holding a plain workflow, a reusable with an input and two
outcomes, a nested reusable and a caller; assert publications and run the plain workflow and the caller.

- [ ] T010 [US1] Create `RECON/Services/Elsa3ImportSelection.cs` (internal), happy path: given the input items and the plan, return the selected source version ids. Model the skipped item now (`Elsa3SkippedItem(Code, FilePath, DefinitionId, Version, Cause, BlockedBy)`, data-model.md) so US3 only adds rules. Register it in the feature.
- [ ] T011 [US1] Create `RECON/Services/ImportedWorkflowPublisher.cs` (internal), research R-6: for each workflow definition in `ReusableActivityImportApplyResult.Workflows`, take its newest version in the input (SemVer). Resolve its target slot (copy `ResolveTargetSlotName` and the slot lookup of `HasActivePublicationOf` from `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Handlers/PublishReconciledWorkflowVersions.cs`, adapted to return the slot, with a comment naming the source). Skip it when `IPublicationRecordStore.ListBySlotAsync(slot.Id)` holds a record of that version in any status but `Failed` (published at most once). Otherwise send `new PublishWorkflow(versionId, ExpectedPublicationId: <the slot's active publication id read for the check, or null>, TenantId: <the operation scope's tenant>)` through `IRequestSender`; on a publication conflict, read the journal again and skip when it now holds the version (another node published first, research R-6). Return published, skipped and failed versions; a failure is logged and never throws. Register it in the feature.
- [ ] T012 [US1] Create `RECON/Services/Elsa3FolderImportTask.cs` (internal, `[Order(4)]`, comment on why 4: research R-2): read the input once (T007), then for each persistence scope through `IPersistenceScopeRunner.RunAsync`, resolve every scoped service from the operation scope (importer, workflow publisher, request sender, grant holder), set the grant to the scope's tenant (T008), build a `ReusableActivityImportCollection` (collection id `elsa3-reconcile:{SourceId}`), analyze, select (T010), apply with `AccessScope = null`, `TenantId` = the scope's tenant and `OperationKey` = `elsa3-reconcile:{SourceId}:{a new id per start}`, then publish workflows (T011). Log the summary line per scope and one information line per conversion warning (contracts/feature-configuration.md). An empty selection logs and returns. Register it in the feature as an `IStartupTask`.
- [ ] T013 [P] [US1] Add fixtures under `TESTS/Fixtures/Reconciliation/`: copy `reusable-two-outcomes.json`, `caller-routes-outcome.json` and `reusable-calls-reusable.json` (nested reusable; an array, which FR-003 allows) from `TESTS/Fixtures`; author `plain-flowchart.json` (a flowchart of two WriteLine activities, the Elsa 3 export shape of the copied fixtures) and `http-endpoint.json` (an HTTP Endpoint start trigger and a WriteHttpResponse with a literal JSON body).
- [ ] T014 [US1] Extend `TESTS/Support/Elsa3ImportHost.cs` with an optional `reconcileFolder` (and `strict`) start parameter that adds `Elsa3JsonReconciliation` to the shell features and its options to the shell settings; keep every existing caller unchanged. Add a helper that lists the workflow publications of a definition id.
- [ ] T015 [US1] Add `TESTS/Elsa3JsonReconciliationTests.cs` for US1 acceptance 1–3: start on an empty store with the fixture folder, with no tenant claim on any request (this proves the startup grant); assert each reusable is published as a graph activity `{n}.0.0`, each workflow and wrapper has an active publication under its Elsa 3 definition id whose source reference carries the scope's tenant (`"default"` in the test host), the caller runs to completion on the route its input selects (use the run helpers the existing `ImportedReusableRunsTests` use), and the plain workflow completes. Shuffle file names to prove order independence.
- [ ] T016 [US1] Add the HTTP trigger scenario (US1 acceptance 4) to `TESTS/Elsa3JsonReconciliationTests.cs`: start with `withHttp: true`, call the imported endpoint and assert the response body the fixture's WriteHttpResponse sends.

**Checkpoint**: US1 runs on its own; this is the demo path.

---

## Phase 4: User Story 2 — restarting with the same input changes nothing (P1)

**Goal**: a restart with unchanged input writes and publishes nothing; new versions and missed publications are
picked up.

**Independent test**: two starts against one persistent store and folder; compare rows and publications.

- [ ] T017 [US2] Make `Elsa3ImportHost` restartable against the same root directory (a `RestartAsync` that disposes the shell and starts a new one on the same SQLite files) in `TESTS/Support/Elsa3ImportHost.cs`, if no such helper exists.
- [ ] T018 [US2] Add restart tests to `TESTS/Elsa3JsonReconciliationTests.cs`: (1) a second start adds or changes no activity version, workflow version or publication record (SC-002); (2) adding a new Elsa 3 version of the plain workflow to the folder imports and publishes only that version; (3) after a start whose workflow publishing was skipped (use the existing publish seam `Elsa3ImportPublishSeamFeature` to refuse once, or delete the publication through the store), the next start publishes it (FR-012); (4) after an operator publishes another version of an imported workflow, or rolls back to an older imported version, the next start leaves that publication active (FR-008); (5) replacing a fixture file with a newer Elsa 3 version of the same workflow imports and publishes the newer version; (6) a reusable refused by the publisher on one start (publish seam) publishes on the next start once the seam allows it, and its callers publish too (FR-012, research R-4); (7) a publication made by another node between the journal check and the request (simulate by publishing the version through `PublishWorkflow` after the check, with a test hook in the publisher) is recognised: no second publication record, no failure (FR-011).
- [ ] T019 [US2] Fix what the restart tests expose in `RECON/Services/*.cs` (expected: none, per research R-4 and R-6); if the unscoped commit is not a no-op on unchanged input, stop and report instead of changing the import's commit.

**Checkpoint**: US1 and US2 pass together.

---

## Phase 5: User Story 3 — unconvertible input is skipped and reported (P2)

**Goal**: failing items and their dependents are skipped with codes; the rest is imported; strict mode fails the start.

**Independent test**: a folder with an unconvertible reusable and its caller beside the US1 fixtures.

- [ ] T020 [US3] Extend `RECON/Services/Elsa3ImportSelection.cs` with the input and analysis rules (research R-5 steps 1–2): a blank source version `Id`, the same `Id` more than once, or the same `(definition id, version)` more than once → `ELS3-RECONCILE-DUPLICATE-INPUT` for each (before analysis, since the analyzer drops these without an item); items with an error diagnostic in the plan → that diagnostic's code (`InvalidSource` for a document with no root); a plan-level error diagnostic with no item fails the start; then remove dependents transitively through the plan items' `Dependencies`, each with `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY` and `BlockedBy`. Define the two codes as constants in a new internal `RECON/Models/Elsa3ReconciliationDiagnosticCodes.cs` (the import's own codes stay in `ReusableActivityImportDiagnosticCodes`, `IMPORT/Elsa3.Activities.Design.Import/Models/ReusableActivityImportPlan.cs`), and list them in the README (T028).
- [ ] T021 [US3] Add the apply loop to `RECON/Services/Elsa3FolderImportTask.cs` (research R-5 step 3): on `ReusableActivityImportValidationException`, map each error diagnostic to its source version through `Metadata["SourceVersionId"]`, falling back to a `SourceVersion` path segment; skip those items and their dependents (T020) and apply again. A validation error with no source version, `CredentialLiteralRefusedException` and any `InvalidOperationException` from the apply (an identity collision among them) fail the start as `Elsa3ReconciliationFailedException`, an unattributed refusal carrying the original message; do not label it a collision. Bounded by the item count.
- [ ] T022 [US3] Report publish refusals in `RECON/Services/ImportedWorkflowPublisher.cs` and the startup task: a reusable whose `Publications` status is not `Published` or `AlreadyPublished` (including `Refused` with the identity-collision code, research R-5) is reported with its code and blocks every workflow that depends on it (wrappers included); those workflows are not published and are reported with `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY` naming the reusable (FR-009).
- [ ] T023 [US3] Implement strict mode in the startup task: with `FailOnUnconvertible`, a skip from T020 throws `Elsa3ReconciliationFailedException` before any apply in that scope; the first refused apply in T021 throws before its commit; refused publications (T022) throw after the publish step, listing the items.
- [ ] T024 [P] [US3] Add `TESTS/Elsa3ImportSelectionTests.cs`: unit tests for T020 over in-memory plans: blank id, duplicate id, duplicate (definition id, version), blocking diagnostic, plan-level diagnostic → fatal, transitive dependents (A ← B ← C), independent items kept, cycle members and dependents; and for T021's locating rule (metadata first, path segment fallback, none → fatal).
- [ ] T025 [P] [US3] Add refusal fixtures under `TESTS/Fixtures/Reconciliation/Refusals/`: a reusable using an Elsa 3 activity without an Elsa 4 counterpart (for example `StateMachine`) and a caller of it; a reusable with `SetOutput` in its body (refused, #3023); a document with no root activity (expect `InvalidSource` from analysis).
- [ ] T026 [US3] Add US3 host tests to `TESTS/Elsa3JsonReconciliationTests.cs`: the unconvertible reusable and its caller are absent and logged with code, file and cause (the caller's cause names the reusable), the summary line counts them, and every US1 item is still imported and published (SC-003); strict mode fails the start and leaves the store empty; a changed plain workflow under an imported Elsa 3 version fails the restart with the import's message and leaves the stored version as it was.

**Checkpoint**: all stories pass.

---

## Phase 6: Polish & cross-cutting

- [ ] T027 [P] Add `TESTS/Elsa3JsonReconciliationFeatureTests.cs`: registration test for the feature (options validation: no `SourceId`, none or two path options; services registered; the authorization context is the decorator and wraps the original registration), as §2.23.1 requires for wiring with logic.
- [ ] T028 [P] Write `RECON/README.md` (what the feature does, configuration from contracts/feature-configuration.md, that removing a file removes nothing, versions `{n}.0.0` and that a host rebuilds its store after a converter upgrade until the converter revision lands (research R-10), logs, strict mode, the two new codes) and `RECON/EXTENSION_POINTS.md` (consumes `IReusableActivityCollectionImporter`, `IPersistenceScopeRunner`, `PublishWorkflow`; contributes one `IStartupTask` at Order 4; decorates `IActivityPublishingAuthorizationContext` with a grant that applies only inside the task's own operation scope; no extension point of its own). Also add the decoration to `Essentials/src/Workflows/Publishing/Api/EXTENSION_POINTS.md` where `IActivityPublishingAuthorizationContext` is listed, if it lists implementations.
- [ ] T029 [P] Add the folder entry point to `Extensions/Elsa3/src/Mapping/README.md` (the "Reusable workflows" and entry-point text) and to `docs/reference/elsa3-migration-readiness.md` (a short "Import from a folder at start" section linking the new README).
- [ ] T030 Build and test only the touched projects through the build-slot wrapper: `RECON`, `IMPORT/Elsa3.Activities.Design.Import`, `TESTS`, `Extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests`, and `Essentials/tests/Architecture/Tests` (composition and architecture guards). Every existing import test passes unchanged (SC-004).
- [ ] T031 Run `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` (and the test-tiers check); commit regenerated solution filters only if they changed.
- [ ] T032 Set the spec's `Status: Implemented` in `specs/235-elsa3-json-reconciliation/spec.md` in the finishing PR, and record any implementation amendments in `plan.md`.

---

## Dependencies & execution order

- **Setup (T001–T002)** → **Foundational (T003–T009)** → **US1 (T010–T016)** → **US2 (T017–T019)** and
  **US3 (T020–T026)** → **Polish (T027–T032)**.
- US2 and US3 both build on US1's task and host helper; they touch different methods of the same files, so run them
  one after the other.
- T003 → T004 (same feature); T005, T006, T008 in parallel; T007 needs T005 and T006; T009 needs T005–T008.
- T010 and T011 are independent; T012 needs both and T008. T013 runs in parallel with T010–T012.

## Parallel examples

- Foundational: T005, T006 and T008 together, while T003–T004 are done in the import.
- US1: T013 (fixtures) beside T010–T012.
- US3: T024 and T025 beside T020–T023.
- Polish: T027, T028 and T029 together.

## Implementation strategy

1. **MVP = US1** (T001–T016): the folder imports and publishes, and the demo path runs. Stop and validate.
2. **US2**: restart safety for persistent and JTest hosts.
3. **US3**: skip-and-report and strict mode.
4. **Polish**: docs, maps, spec status; then the review loop on the draft PR, evidence, ready, and Code Review merges.

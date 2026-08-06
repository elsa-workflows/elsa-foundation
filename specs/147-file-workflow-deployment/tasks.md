# Tasks: File-based workflow deployment at startup

**Input**: Design documents from `/specs/147-file-workflow-deployment/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ — all present.

**Tests**: included (explicitly required by spec FR-011 and constitution §2.23.1/.2). House rules: xunit only, hand-written stubs, no FluentAssertions.

**Organization**: grouped by the spec's user stories. US1 = packaging/import (P1, MVP), US2 = publish-on-reconcile (P1), US3 = FolderPath (P2), US4 = docs (P3). The e2e suite exercises US1+US2+US3 together and lives in the final phase.

## Phase 1: Setup

**Purpose**: pin a green baseline so §2.21.1 regressions are attributable.

- [X] T001 Baseline run: `dotnet test tests/Elsa/Workflows/Design/Tests`, `dotnet test tests/Elsa/Architecture`, and the Publishing test project(s) under `tests/Elsa/Workflows/Publishing/` — record pass counts before any change.

---

## Phase 2: Foundational

**None.** No shared blocking infrastructure: every prerequisite (reconciliation pipeline, publish engine, readiness gate, task/lock machinery) already exists. Stories start directly after Phase 1.

---

## Phase 3: User Story 1 — Deploy workflow definitions from mounted files, zero API calls (P1) 🎯 MVP

**Goal**: `JsonWorkflowReconciliation` in shell configuration activates in `Elsa.Workbench` instead of being silently skipped; definitions in configured files are imported at startup.

**Independent Test**: start Workbench with env vars composing the feature (`…Options__SourceId`, `…Options__FilePath`) pointing at a definition file; after `/health/ready` = 200, `GET /design/workflows/definitions?name=…` returns the definition.

### Implementation

- [X] T002 [US1] Add `ProjectReference`s for `..\..\Elsa\Workflows\Design\Reconciliation\Elsa.Workflows.Design.Reconciliation.csproj` and `..\..\Elsa\Workflows\Design\Reconciliation\Json\Elsa.Workflows.Design.Reconciliation.Json.csproj` (with the conventional why-comment: workflow-side reconciliation pass + JSON file source for file-based deployment) to `src/Apps/Elsa.Workbench/Elsa.Workbench.csproj`, next to the activities reconciliation refs (~L51-52).
- [X] T003 [US1] In `src/Apps/Elsa.Workbench/Program.cs`: add `using Elsa.Workflows.Design.Reconciliation;` and `using Elsa.Workflows.Design.Reconciliation.Json;`; add `typeof(WorkflowsDesignReconciliationFeature).Assembly,` and `typeof(JsonWorkflowReconciliationFeature).Assembly,` to the main `.WithAssemblies(...)` block beside the activities reconciliation entries (~L240-243), with a comment mirroring the existing one (Design-side workflow reconciliation + JSON file source). Do **not** enable the feature in `shells.json`/`shells.baseline.json` (it requires SourceId + a path; empty options fail registration).
- [X] T004 [P] [US1] Add guard fact `Server_catalogs_workflow_json_reconciliation_for_file_based_deployment` to `tests/Elsa/Architecture/ArchitectureGuardTests.cs` following the `Server_catalogs_graph_design_separately_from_graph_runtime` template (~L371): assert the two csproj reference names and the two exact `typeof(…).Assembly` substrings in `Program.cs`; comment references spec 147 / issue #1157.
- [X] T005 [US1] Verify: `dotnet build src/Apps/Elsa.Workbench/Elsa.Workbench.csproj` and `dotnet test tests/Elsa/Architecture` green; confirm `tests/Elsa/Architecture/Baselines/ef-core-surface.json` ratchet unchanged (reconciliation family is EF-free) — regenerate via `EfCoreSurfaceRatchetTests` flow only if it legitimately shifted.
- [X] T006 [US1] Smoke (quickstart A, steps 2–5 import half): temp folder + single-envelope file, start Workbench with `CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__{SourceId,FilePath}` env vars, poll `/health/ready`, assert the definition via `GET /design/workflows/definitions?name=…`; also confirm the old failure mode is gone (no "requested feature(s) not available" log line).

**Checkpoint**: file-configured import works end to end — the MVP increment.

---

## Phase 4: User Story 2 — Reconciled definitions are published and executable (P1)

**Goal**: `PublishOnReconcile: true` publishes the latest reconciled version of each source-owned definition after a successful pass — idempotent, single-node, failure-isolated, before readiness.

**Independent Test**: same smoke as US1 plus `…Options__PublishOnReconcile=true`; after readiness, `GET /publishing/workflows/{definitionId}/slots` shows an Active publication and `POST /runtime/workflows/executables/{artifactId}/execute` completes; restart ⇒ same `activePublicationId`.

### Implementation — event pipeline (Design reconciliation domain)

- [X] T007 [P] [US2] Create `WorkflowVersionSourceClaim` sealed record (`DefinitionId`, `Version`, `SemVerSortKey`, `SourceId`, `SourceKind`, `PublishRequested`, `Deleted`) in `src/Elsa/Workflows/Design/Reconciliation/Core/WorkflowVersionSourceClaim.cs` per data-model §3.
- [X] T008 [P] [US2] Create `OnWorkflowVersionsReconciled` sealed class : `IEvent` (ctor-carried `IReadOnlyList<WorkflowVersionSourceClaim> Claims`) in `src/Elsa/Workflows/Design/Reconciliation/Core/OnWorkflowVersionsReconciled.cs`, XML docs quoting delivery strategy (Sequential) and subscriber-must-not-throw policy per contracts/on-workflow-versions-reconciled.md.
- [X] T009 [US2] Extend `src/Elsa/Workflows/Design/Reconciliation/Core/OnWorkflowVersionsReconciling.cs` with `public ICollection<WorkflowVersionSourceClaim> Claims { get; } = [];` (fan-in payload shape, additive).
- [X] T010 [US2] Add default interface member `bool RequestsPublication => false;` (XML-doc'd) to `src/Elsa/Workflows/Design/Reconciliation/Contracts/IWorkflowReconciliationSource.cs`.
- [X] T011 [US2] In `src/Elsa/Workflows/Design/Reconciliation/Handlers/WorkflowVersionsReconcilingHandler.cs`, add one claim per contributed entry (DefinitionId taken from the factory-resolved definition; `SemVer.ToSortKey(entry.Version)`; source's `SourceId`/`SourceKind`/`RequestsPublication`; `entry.Deleted`) in the same loop that adds versions.
- [X] T012 [US2] In `src/Elsa/Workflows/Design/Reconciliation/Services/WorkflowsVersionReconciler.cs`, after the per-version loop completes without throwing, publish `new OnWorkflowVersionsReconciled([..@event.Claims])` via the existing `IInlineEventPublisher`; not published on a failed pass.
- [X] T013 [US2] Add `public bool PublishOnReconcile { get; set; }` (default false, XML-doc'd) to `src/Elsa/Workflows/Design/Reconciliation/Json/Options/JsonWorkflowReconciliationOptions.cs`; override `RequestsPublication => _options.PublishOnReconcile` in `src/Elsa/Workflows/Design/Reconciliation/Json/Services/JsonWorkflowReconciliationSource.cs`.

### Implementation — subscriber (Publishing engine)

- [X] T014 [US2] Add `ProjectReference` to `..\Design\Reconciliation\Core\Elsa.Workflows.Design.Reconciliation.Core.csproj` in `src/Elsa/Workflows/Publishing/Elsa.Workflows.Publishing.csproj` (why-comment: subscribes to the reconcile completion event — allowed Publishing→Design-contract direction). Do **not** touch `Publishing/Persistence/Groundwork` (exact-equality-pinned).
- [X] T015 [US2] Create `PublishReconciledWorkflowVersions` (`public sealed class : IEventHandler<OnWorkflowVersionsReconciled>`) in `src/Elsa/Workflows/Publishing/Handlers/PublishReconciledWorkflowVersions.cs` implementing data-model §4: group claims by DefinitionId → highest `SemVerSortKey` → skip `!PublishRequested`/`Deleted`/definition soft-deleted → resolve version row via `IWorkflowDefinitionVersionStore.ListByDefinitionAsync` filtered on sort key (warn if absent) → slot pre-check scoped to the policy-resolved target slot (`IPublicationPolicyStore` + `IPublicationPolicyResolver` with a `null` request intent, then `IPublicationSlotStore.FindAsync` + `IPublicationRecordStore.FindAsync`; skip when that slot's Active record's `WorkflowDefinitionVersionId` matches) → `IRequestSender.Send(new PublishWorkflow(versionId))` → per-definition try/catch with structured error logs; `Handle` never throws.
- [X] T016 [US2] Register in `src/Elsa/Workflows/Publishing/WorkflowsPublishingFeature.cs`: `services.AddEventHandler<OnWorkflowVersionsReconciled, PublishReconciledWorkflowVersions>();` beside the two existing `AddEventHandler` calls.

### Tests

- [X] T017 [P] [US2] Extend `tests/Elsa/Workflows/Design/Tests/Unit/Reconciliation/WorkflowVersionsReconcilingHandlerTests.cs`: claims populated per entry (multi-source provenance, `RequestsPublication` passthrough, generated-DefinitionId recorded, `Deleted` flag).
- [X] T018 [P] [US2] Extend `tests/Elsa/Workflows/Design/Tests/Unit/Reconciliation/WorkflowsVersionReconcilerTests.cs` (CapturingSender pattern): `OnWorkflowVersionsReconciled` published with claims after a successful pass; **not** published when a version reconcile throws.
- [X] T019 [P] [US2] New `PublishReconciledWorkflowVersionsTests` in the Publishing engine test project under `tests/Elsa/Workflows/Publishing/` (create the mirrored test project + `Elsa.Server.slnx` entry if the engine has none — Api tests exist at `tests/Elsa/Workflows/Publishing/Api/Tests/`): branch coverage per §2.23.2 — publishes latest claim only; skips non-requested; skips deleted; warns on missing version row; slot pre-check skip (including: a non-target slot holding the version does not suppress the publish, and a policy-moved target slot does); publish invoked with correct VersionId; one failing definition doesn't stop the rest; `Handle` never throws. Hand-written stubs for the policy/slot/record stores, version store, and `IRequestSender`.
- [X] T020 [P] [US2] Registration tests (§2.23.1): extend `tests/Elsa/Workflows/Design/Tests/Unit/Reconciliation/Json/JsonWorkflowReconciliationFeatureTests.cs` (options with `PublishOnReconcile` still register; default false) and the `WorkflowsPublishingFeature` registration test (new handler resolves as `IEventHandler<OnWorkflowVersionsReconciled>`).
- [X] T021 [US2] Update `src/Elsa/Workflows/Design/Reconciliation/EXTENSION_POINTS.md`: `### OnWorkflowVersionsReconciled` entry (content from contracts/on-workflow-versions-reconciled.md), `Claims` addition under the Reconciling entry, `RequestsPublication` under the `IWorkflowReconciliationSource` entry with tagged Known implementations — `CatalogParityTests` must pass.
- [X] T022 [P] [US2] Update `src/Elsa/Workflows/Publishing/README.md` (new **Cross-domain contributions** section: subscriber → Design reconciliation event, link to the catalog) and `src/Elsa/Workflows/Publishing/EXTENSION_POINTS.md` (cross-domain seams consumed: the completion event).
- [X] T023 [US2] Run `dotnet test tests/Elsa/Workflows/Design/Tests`, Publishing test project(s), `dotnet test tests/Elsa/Architecture` — all green, zero modified existing assertions (§2.21.1).

**Checkpoint**: import + publish works with `FilePath`; restart idempotency observable.

---

## Phase 5: User Story 3 — Point at a folder of definition files (P2)

**Goal**: `FolderPath` scans `*.json` deterministically; exactly-one-of-three validation; empty folder tolerated, missing folder actionable.

**Independent Test**: US1 smoke with `…Options__FolderPath` at a multi-file temp dir; all files imported in ordinal name order; both-options misconfiguration fails activation with the exactly-one message.

### Implementation

- [X] T024 [US3] Add `public string? FolderPath { get; set; }` (XML docs: top-level non-recursive `*.json`, ordinal file-name order, empty-folder/missing-folder semantics per contracts/shells-configuration.md) to `src/Elsa/Workflows/Design/Reconciliation/Json/Options/JsonWorkflowReconciliationOptions.cs`.
- [X] T025 [US3] Rewrite `ValidateOptions()` in `src/Elsa/Workflows/Design/Reconciliation/Json/JsonWorkflowReconciliationFeature.cs` as a count over `{FilePath set, Files.Any(), FolderPath set}` — `!= 1` throws `InvalidOperationException` naming all three options and the exactly-one rule; SourceId check unchanged; existing accept/reject cases preserved verbatim.
- [X] T026 [US3] Extend `EffectiveFiles()` in `src/Elsa/Workflows/Design/Reconciliation/Json/Services/JsonWorkflowReconciliationSource.cs`: `FolderPath` branch — missing dir ⇒ `InvalidWorkflowCatalogJsonException(folderPath, "the folder does not exist.")`; `Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal)` → sequential `JsonWorkflowReconciliationFileOption`s; empty scan ⇒ info log + empty result. In `Read`, warn per envelope with null/blank `DefinitionId` (file + name in the message).

### Tests

- [X] T027 [P] [US3] Extend `tests/Elsa/Workflows/Design/Tests/Unit/Reconciliation/Json/JsonWorkflowReconciliationFeatureTests.cs` with the full validation matrix from contracts/shells-configuration.md (9 rows: none/each-single/every-pair/all-three, plus SourceId row) — existing facts untouched.
- [X] T028 [P] [US3] Extend `tests/Elsa/Workflows/Design/Tests/Unit/Reconciliation/Json/JsonWorkflowReconciliationSourceTests.cs` (temp-dir IO pattern already used there): ordinal name ordering (e.g. `10.json` < `2.json` ordinal check), non-json entries ignored, subdirectory json NOT read, empty folder ⇒ empty + no throw, missing folder ⇒ `InvalidWorkflowCatalogJsonException`, null-definitionId warning captured via `CapturingLogger`.
- [X] T029 [US3] Run `dotnet test tests/Elsa/Workflows/Design/Tests` — green, existing assertions unmodified.

**Checkpoint**: acceptance-shaped configuration (`FolderPath` + `PublishOnReconcile`) fully functional.

---

## Phase 6: User Story 4 — Author and operate with confidence (docs) (P3)

**Goal**: an author/operator can go from zero to a deployed, executable workflow following docs alone.

**Independent Test**: follow the docs from scratch (quickstart B); the acceptance flow passes.

- [X] T030 [P] [US4] Create `src/Elsa/Workflows/Design/Reconciliation/Json/README.md` per the `Clr/README.md` precedent: what the feature provides; options table (`SourceId`, `FilePath`, `Files`, `FolderPath` scan rules, `PublishOnReconcile` + `WorkflowsPublishing` prerequisite); registration/shells snippet; definition-file authoring guidance from contracts/definition-file-format.md (pin `definitionId`, `actver_*` recipe, immutable versions, deletion semantics); failure modes; constitutional basis.
- [X] T031 [P] [US4] Add a "Deploying workflow definitions from files" section to `docs/docker-hub-quickstart.md` after "Mount it — two modes": second mount (`-v ./defs:/app/workflow-definitions:ro`), shells.json feature snippet, readiness note (`/health/ready` gates deployment completion, `/` does not), link to the Json README for file authoring.
- [X] T032 [P] [US4] Add the definitions-folder row to the "### Mounts" table in `docs/docker.md` (+ one-line pointer to the quickstart section).
- [X] T033 [US4] Regenerate `docs/maps/*` via the `tools/maps` generator (feature/dependency/project-reference maps shift from T002/T014); commit regenerated output only.

**Checkpoint**: docs deliverable complete.

---

## Phase 7: Polish — e2e suite & final validation

**Purpose**: FR-011's e2e test (spans US1+US2+US3) and the SC sweep.

- [X] T034 Create `e2e-tests/file-deployment/_FileDeploymentCommon.ps1` (dot-sources `../_ElsaCommon.ps1`; helpers: write-definition-file from a resolved `actver_*` id via `Get-ActivityVersionId`; durability-style `Start/Stop-ElsaServer` variants that pass the `CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__*` env vars; `Wait-Ready` polling `GET /health/ready` for 200).
- [X] T035 Create `e2e-tests/file-deployment/Test-FileBasedDeployment.ps1` per research D7: Phase A resolve id + author file into a temp folder; Phase B restart server with the feature composed (`SourceId`, `FolderPath`, `PublishOnReconcile=true`); wait `/health/ready`; assert `GET /design/workflows/definitions?name=…`, `GET /publishing/workflows/{definitionId}/slots` (Active + artifactId), `POST /runtime/workflows/executables/{artifactId}/execute` (with `sourceReferenceId`) completes; restart with unchanged folder ⇒ same version count + same `activePublicationId` (SC-002); cleanup restores the normal server. Standard param block + pass/total tally + exit-code convention.
- [X] T036 Create `e2e-tests/file-deployment/README.md` (purpose, script table, composition mechanism note per e2e README's "Composition change" precedent) and register the suite in `e2e-tests/README.md`'s categorization + scripts tables.
- [X] T037 Run the e2e suite via `powershell -NoProfile -ExecutionPolicy Bypass -File e2e-tests/file-deployment/Test-FileBasedDeployment.ps1` (schema-deploy prerequisites per `e2e-tests/README.md`); fix findings.
- [X] T038 Final sweep: full `dotnet test` for Design, Publishing, Architecture test projects; walk quickstart.md section A incl. step 7 failure surfaces; check every SC-001…SC-006 box against evidence; confirm no `[NEEDS CLARIFICATION]`/TODO left in specs/147 artifacts.

---

## Dependencies & Execution Order

- **Phase 1** → everything.
- **US1 (T002–T006)**: independent; MVP. T004 parallel to T002/T003; T005/T006 after T002–T004.
- **US2 (T007–T023)**: independent of US1 at code level (touches reconciliation + publishing packages, not the host), but its smoke verification needs US1's packaging — run T006-style verification after both. Order inside: T007/T008 [P] → T009–T013 (T010 before T011; T013 after T010) → T014 → T015 → T016 → tests T017–T020 [P] → docs T021/T022 → T023.
- **US3 (T024–T029)**: independent of US2; touches the same Json files as T013 — if run in parallel with US2, coordinate edits to `JsonWorkflowReconciliationOptions.cs`/`JsonWorkflowReconciliationSource.cs` (prefer sequential US2 → US3).
- **US4 (T030–T033)**: T030–T032 parallel; T033 after T002+T014 land.
- **Phase 7 (T034–T038)**: needs US1+US2+US3 complete; T034–T036 parallelizable, then T037, then T038.

## Parallel Example: User Story 2

```text
# After T007/T008 (parallel), then the pipeline edits sequentially, then:
T017 handler claims tests        (Design tests)
T018 reconciler event tests      (Design tests)
T019 subscriber tests            (Publishing tests)
T020 registration tests          (both)
# — all four in parallel, then T021–T023.
```

## Implementation Strategy

MVP first: Phase 1 → US1 (T002–T006) → validate independently (import works, feature no longer skipped) → US2 (the deployment story becomes real) → US3 (acceptance-shaped config) → US4 docs → Phase 7 e2e + sweep. Commit after each task or logical group (auto-commit hooks fire per speckit phase). Sequential single-developer order is simply T001→T038.

**Total**: 38 tasks — US1: 5, US2: 17, US3: 6, US4: 4, Setup: 1, Polish: 5.

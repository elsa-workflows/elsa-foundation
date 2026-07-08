---
description: "Task list — Workflow-Definition GitOps (085)"
---

# Tasks: Workflow-Definition GitOps — Git Reconciliation Source + Export Sink

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/contracts.md](contracts/contracts.md),
[quickstart.md](quickstart.md).

**Tests**: INCLUDED — the spec's acceptance scenarios (US2–US4) and the contract test obligations are
explicit deliverables.

**Organization**: by phase then user story. US1 (shared `Elsa.Git`) is already DELIVERED — its only
residual (the `<ProjectReference>`) is folded into Setup. Foundational = the shared reconciler-seam
changes both the source and exporter depend on (FR-008a, additive model/facade fields, base startup-task
registration, hash tripwire).

## Path conventions
`src/Elsa/Workflows/Design/...` for product code; `tests/Elsa/Workflows/Design/...` for tests. New git
project rooted at `src/Elsa/Workflows/Design/Reconciliation/Git/`.

---

## Phase 1 — Setup

- [x] T001 Create the new leaf project `src/Elsa/Workflows/Design/Reconciliation/Git/Elsa.Workflows.Design.Reconciliation.Git.csproj` (`net10.0`, `ImplicitUsings`+`Nullable` enable) with `<ProjectReference>`s to `Elsa.Workflows.Design.Reconciliation`, `Core/Elsa.Workflows.Design.Reconciliation.Core`, `..\..\Core\Elsa.Workflows.Design.Core`, `..\..\Persistence\Core\Elsa.Workflows.Design.Persistence.Core`, `..\..\..\..\Serialization\Core\Elsa.Serialization.Core`, `..\..\..\..\Tasks\Core\Elsa.Tasks.Core`, `..\..\..\..\Locking\Core\Elsa.Locking.Core`, `..\..\..\..\Git\Elsa.Git.csproj`; `PackageReference`s `CShells.Abstractions` + the manifest-generator hints package (per `WorkflowDesignValidationsFeature`).
- [x] T002 Add `<Compile Remove="Git/**/*" />` (+ `EmbeddedResource`/`None` Remove) to `src/Elsa/Workflows/Design/Reconciliation/Elsa.Workflows.Design.Reconciliation.csproj` so the nested `Git/` folder compiles as its own assembly (mirror the existing `Core/**` glob block).
- [x] T003 Add the new project to the solution and confirm `dotnet build` on it (empty) succeeds and the dependency-envelope arch guard still passes (`ArchitectureGuardTests`).

---

## Phase 2 — Foundational (shared reconciler seam) — BLOCKS US2/US3

**Goal**: FR-008a fix + additive fields + base lifecycle activation + Model X tripwire, all with the
existing reconciler tests staying green.

- [x] T004 [P] Add `string? ContentHash = null` and `bool Deleted = false` to `WorkflowVersionReconciliationModel` in `src/Elsa/Workflows/Design/Reconciliation/Models/WorkflowVersionReconciliationModel.cs` (additive record params; update XML doc).
- [x] T005 [P] Add read-only `DateTimeOffset? DeletedAt { get; }` to `IWorkflowDefinition` in `src/Elsa/Workflows/Design/Core/Contracts/IWorkflowDefinition.cs` (document: definition-level lifecycle metadata, NOT authored content / §E2.9). Update the read-model implementation(s) of `IWorkflowDefinition` to expose it.
- [x] T006 [P] Add `bool deleted = false` param to `IWorkflowDefinitionFactory.Create` in `src/Elsa/Workflows/Design/Core/Contracts/IWorkflowDefinitionFactory.cs` and its implementation, stamping `DeletedAt` from `deleted` via injected `TimeProvider` (find impl: `grep -rl "IWorkflowDefinitionFactory" src`).
- [x] T007 Pass `entry.Deleted` to `definitionFactory.Create(...)` in `WorkflowVersionsReconcilingHandler.Handle` (`src/Elsa/Workflows/Design/Reconciliation/Handlers/WorkflowVersionsReconcilingHandler.cs`).
- [x] T008 Update `WorkflowDefinition.From(IWorkflowDefinition)` in `src/Elsa/Workflows/Design/Persistence/Core/Entities/WorkflowDefinition.cs` to read `source.DeletedAt` off the interface (so the fresh-create path honors soft-delete), keeping the existing entity-source branch.
- [x] T009 **FR-008a**: in `WorkflowsVersionReconciler.ReconcileVersion` (`src/Elsa/Workflows/Design/Reconciliation/Services/WorkflowsVersionReconciler.cs`) MOVE the `UpdateDefinitionMetadata` call to AFTER the outdated-version skip: order = find definition → compute `candidateSortKey` + `FindLatestVersionAsync` → outdated-skip `return` → then create-if-absent / else `UpdateDefinitionMetadata`. (The skip guarantees the newest-or-equal gate — no extra condition.)
- [x] T010 **R10**: widen `UpdateDefinitionMetadata` to reconcile `DeletedAt` — set to `TimeProvider.GetUtcNow()` when incoming is deleted and persisted is live; clear to `null` when incoming is live and persisted is soft-deleted; keep it idempotent (no save when nothing changed). Inject `TimeProvider`.
- [x] T011 **R13 (Model X tripwire)**: on the duplicate `(id,version)` path, load the existing version via `IWorkflowDefinitionVersionStore` (`ListByDefinitionAsync` → match `SemVerSortKey`; its `State` is hydrated by the read store), serialize incoming `version.State` and existing `State` via injected `IPayloadSerializer`, and on mismatch log a Warning ("same (id,version), different content — broken source"). `DuplicateHandling.Throw` still throws as today; the dedicated hash-mismatch throw stays deferred to FR-016a. Add `IPayloadSerializer` to the ctor.
- [x] T012 **R3**: register `services.AddScoped<IStartupTask, WorkflowsVersionReconcilerStartupTask>()` in `WorkflowsDesignReconciliationFeature.ConfigureServices` (`src/Elsa/Workflows/Design/Reconciliation/WorkflowsDesignReconciliationFeature.cs`); add the `Elsa.Tasks.Core` using/ref if not already present.
- [x] T013 [P] [TEST] Extend `tests/Elsa/Workflows/Design/Tests/Unit/Reconciliation/WorkflowsVersionReconcilerTests.cs`: (a) FR-008a — persisted `2.0.0`, incoming `1.0.0` with a rename → `saveDef.Saved` is empty, no version added; (b) newest-entry rename still applies (guard the existing case); (c) R10 — incoming `Deleted=true` sets `DeletedAt`, incoming `Deleted=false` on a soft-deleted def clears it, no version row deleted; (d) R13 — same `(id,version)` different `State` logs a warning under `Skip` (assert via a capturing logger) and still no add. Extend the stubs (`StubVersionStore.ListByDefinitionAsync`, add `IPayloadSerializer` + `TimeProvider`).
- [x] T014 Run `dotnet test tests/Elsa/Workflows/Design/Tests/... --filter Reconciliation` — all existing + new reconciler tests green.

**Checkpoint**: reconciler seam correct and lifecycle-armed, independent of git.

---

## Phase 3 — US2: Versions authored in git appear in the catalog (P1)

**Goal**: a Consumer-role feature imports `versions/*.json` + `definition.json` into the catalog.
**Independent test**: seed a temp repo with two versions + `definition.json`, run one pass, assert both
`WorkflowDefinitionVersion` rows exist with correct SemVer + committer-date `SourceCreatedAt` and the
definition name matches.

- [x] T015 [P] [US2] Add config enums `GitReconciliationRole`, `GitCredentialsMode`, `GitPushMode` in `src/Elsa/Workflows/Design/Reconciliation/Git/Options/`.
- [x] T016 [P] [US2] Add `GitReconciliationOptions` + `GitExportOptions` (bound properties: `RemoteUrl`, `Branch`, `WorkflowsPath`, `LocalCachePath`, `Role`, `CredentialsMode`, `KeyPath`, `Token`, `Export.{PushMode,Branch,Tag}`; a resolved `SourceId` = `{RemoteUrl}#{Branch}`, a resolved `LocalCachePath`) in `src/Elsa/Workflows/Design/Reconciliation/Git/Options/`.
- [x] T017 [P] [US2] Implement `GitCanonicalJson` in `src/Elsa/Workflows/Design/Reconciliation/Git/Services/GitCanonicalJson.cs`: `ToCompact(WorkflowDefinitionState, IPayloadSerializer)`, `Indent(string compact)` (JsonNode pure-whitespace transform), `CompactFromFile(string fileText)` (`JsonNode.Parse(...).ToJsonString()`), `Sha256Hex(string)`.
- [x] T018 [P] [US2] [TEST] `GitCanonicalJsonTests`: `hash(CompactFromFile(Indent(x))) == hash(x)`; string-internal whitespace ("Send Email") preserved through indent+compact round-trip.
- [x] T019 [US2] Define `IGitWorkspace` (`Contracts/IGitWorkspace.cs`) and implement `GitWorkspace` (`Services/GitWorkspace.cs`): `EnsureReadyAsync` = clone-if-absent (`clone --branch --single-branch`) → apply credentials into the clone git config (R8: `core.sshCommand` for SshKey; credential-store file for Token; no-op for HostDefault) → role integrate (Writer: `fetch` + `merge --ff-only`, throw-and-surface on non-ff, never `reset --hard`; Consumer: `fetch` + `reset --hard origin/{Branch}`). Uses `IGitClient`.
- [x] T020 [US2] Implement `GitWorkflowReconciliationSource : IWorkflowReconciliationSource` (`Services/GitWorkflowReconciliationSource.cs`): `SourceKind="git"`, `SourceId` from options; `Read` = `EnsureReadyAsync` → enumerate `{WorkflowsPath}/*/versions/*.json` → per file deserialize `State` (`IPayloadSerializer`), read sibling `definition.json` (name/description/deleted; malformed→diagnostic+fallback), `SourceCreatedAt` via `IGitClient.RunOrDefault(repo,"log","-1","--format=%cI","--",relPath)` parsed as `DateTimeOffset?`, `ContentHash` via `GitCanonicalJson` → emit one `WorkflowVersionReconciliationModel`. Malformed version file → skip + log.
- [x] T021 [US2] Implement `WorkflowsDesignGitReconciliationFeature : WorkflowsDesignReconciliationFeature` (`WorkflowsDesignGitReconciliationFeature.cs`) with `[ShellFeature(name:"WorkflowsDesignGitReconciliation")]` + manifest category/runtime attributes + `[ManifestSetting]` bound properties (Token `Secret=true`). `ConfigureServices`: `base.ConfigureServices(services)`, register `Options.Create(...)`, `IGitWorkspace→GitWorkspace`, override `Sources` to yield the git source (register the source as `IWorkflowReconciliationSource`). Consumer path only in this task; Writer wiring in US3.
- [x] T022 [US2] [TEST] `GitWorkflowReconciliationSourceTests` (temp local repo via `IGitClient`): two versions + `definition.json` → two models, correct SemVer, committer-date `SourceCreatedAt`, populated `ContentHash`, name from `definition.json`; malformed version file skipped; `deleted:true` → `model.Deleted`.
- [x] T023 [US2] [TEST] Integration-style pass: source → `WorkflowVersionsReconcilingHandler` → `WorkflowsVersionReconciler` against stub stores asserts both version rows created (US2 #1/#2), rename applied (US2 #3), soft-delete propagated with no row deletion (US2 #4), and a second pass adds zero rows (US2 #5, idempotent).

**Checkpoint**: US2 independently testable and complete.

---

## Phase 4 — US3: The writer mirrors its catalog versions to git (P2)

**Goal**: Writer export reconciler makes git's version files match the catalog.
**Independent test**: author two versions, run export, assert two committed files whose canonical
`state` re-hashes to the version hash, machine-authored.

- [x] T024 [US3] Define `IGitWorkflowExporter` (`Contracts/IGitWorkflowExporter.cs`) and implement `GitWorkflowExporter` (`Services/GitWorkflowExporter.cs`): `ExportAsync` = `EnsureReadyAsync` → `IWorkflowDefinitionStore.ListAsync(all)` → per definition `IWorkflowDefinitionVersionStore.ListByDefinitionAsync` → for each version write `versions/{semver}.json = Indent(compact State)` only if absent; refresh `definition.json` when `{name,description,deleted}` differs from disk; stage ONLY touched files, commit with machine identity (`-c user.name/-c user.email`, `Elsa Design <design@elsa.local>`) + message `Publish {name} v{version} ({definitionId})`, optional tag `wf/{definitionId}/v{version}`; then `PushMode==Immediate` → `git push --ff-only origin {ExportBranch}` (refuse on divergence). Never runs when `Role!=Writer`.
- [x] T025 [US3] Implement `GitWorkflowExportStartupTask : IStartupTask` (`Startup/GitWorkflowExportStartupTask.cs`) `[SingleNodeTask] [Order(3)]`, acquiring a distributed lock (mirror `WorkflowsVersionReconcilerStartupTask`) and calling `IGitWorkflowExporter.ExportAsync`.
- [x] T026 [US3] In `WorkflowsDesignGitReconciliationFeature.ConfigureServices`, when `Role==Writer` register `IGitWorkflowExporter→GitWorkflowExporter` + `IStartupTask→GitWorkflowExportStartupTask`; when `Consumer`, register neither.
- [x] T027 [US3] [TEST] `GitWorkflowExporterTests` (temp repo): absent catalog versions → exactly those files written+committed by the machine identity; present versions skipped; second run no-op (idempotent, US3 #1); each written `state` re-hashes to the persisted version hash; `PushMode=Manual` leaves commits local (US3 #2); `Immediate` push refused on a divergent remote (US3 #3).
- [x] T028 [US3] [TEST] `WorkflowsDesignGitReconciliationFeatureTests`: `Role=Writer` registers exporter + export startup task; `Role=Consumer` registers neither; the base registers the import startup task (R3); Token bound as secret.

**Checkpoint**: US3 independently testable and complete.

---

## Phase 5 — US4: Round-trips without looping or corrupting (P2)

**Goal**: writer export + consumer import compose with no ping-pong; single-writer violation is loud.
**Independent test**: export a version, import on Writer and Consumer → both no-ops; simulate a second
writer → rejected push or loud import.

- [x] T029 [US4] [TEST] Round-trip: export a version to a temp repo, then run an import pass on a Writer clone and on a Consumer clone → reconciliation is a no-op for that version (US4 #1); structural loop-avoidance holds across a second export+import.
- [x] T030 [US4] [TEST] Single-writer: two writers mint `v2.0.0` with different content → the second `push --ff-only` is rejected (US4 #2 first half); and an import seeing same `(id,version)` different `State` logs the R13 warning / throws under `Throw` (US4 #2 second half / #3).
- [x] T031 [US4] [TEST] Edge cases: unreachable remote fails fast with catalog untouched; malformed version file skipped while siblings reconcile; a non-ff Writer integrate surfaces (never `reset --hard`).

**Checkpoint**: US4 complete; all acceptance scenarios covered.

---

## Phase 6 — Polish & QA gate

- [x] T032 [P] ~~Add a commented `WorkflowsDesignGitReconciliation` sample block to `src/Apps/Elsa.Server/shells.baseline.json`~~ **DEVIATED**: CShells treats presence in the features map as *enabled* (there is no disabled/commented form in strict JSON), so a sample entry would activate the feature at startup against an empty `RemoteUrl` and fail the reconcile pass. The config example lives in `Git/README.md` instead; `shells.baseline.json` is intentionally left untouched.
- [x] T033 [P] Add a short `Git/README.md` (+ an `EXTENSION_POINTS.md` note if the reconciliation folder has one) describing roles, clone modes, credentials, and the on-disk layout.
- [x] T034 [P] DRY sweep: extract any duplicated git-path/JSON helpers into `GitCanonicalJson` / a small path helper; confirm no repeated arrange blocks in the new tests (instance fields + ctor setup, `IAsyncDisposable` teardown for temp repos).
- [x] T035 Full QA gate: `dotnet build` (solution) + `dotnet test` on `Elsa.Workflows.Design.Tests` and the new git test project + the architecture guard suite (dependency-envelope 49/49). Fix any regressions.

---

## Dependencies & order
- Setup (T001–T003) → Foundational (T004–T014) → US2 (T015–T023) → US3 (T024–T028) → US4 (T029–T031) → Polish (T032–T035).
- Foundational BLOCKS US2/US3 (source + exporter consume the model/facade/reconciler changes).
- US3 depends on US2's feature + workspace + canonicalizer.
- Within a phase, `[P]` tasks touch different files and may run in parallel (e.g. T004/T005/T006; T015/T016/T017/T018).

## Parallel example (Foundational)
`T004` (model), `T005` (facade), `T006` (factory) are independent files → parallel; then `T007–T012`
(sequential, same reconciler/handler files) → `T013` tests → `T014` run.

## MVP scope
Foundational + US2 (Consumer import) is the minimal shippable slice: git-authored versions land in the
catalog with the FR-008a correctness fix. US3/US4 add the Writer export + coherence hardening.

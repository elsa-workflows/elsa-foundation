# Tasks: Publishing engine / API split

**Input**: Design documents from `specs/145-publishing-engine-split/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the spec explicitly requires them (framework §2.23 registration + implementation tests, §2.21.1 golden-rule preservation).

**Nature of this work**: A behaviour-preserving refactor. Unlike a greenfield feature, the split lands as **one atomic foundational change** (Phase 2) — the existing test suite (US2) cannot even compile until the move is complete. The three user stories are therefore the split's **validations/assertions**, layered on top of the foundational move rather than independently shippable slices. Ordering reflects that.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: different files, no dependency on an incomplete task
- **[Story]**: US1 / US2 / US3 (Setup, Foundational, Polish carry no story label)

---

## Phase 1: Setup

**Purpose**: Scaffold the new engine package and wire it into the build.

- [X] T001 Create `src/Elsa/Workflows/Publishing/Elsa.Workflows.Publishing.csproj` (net10.0) mirroring the Api envelope minus FastEndpoints + Api.Capabilities. (Also created a `WorkflowsPublishingFeature : IShellFeature` stub, `[ShellFeature("WorkflowsPublishing")]`, `DependsOn { WorkflowsRuntimeTriggers, Events }`, empty `virtual ConfigureServices` pending Phase 2.)
- [X] T002 Add the new project to `Elsa.Server.slnx` under the `/src/Elsa/Workflows/Publishing/` solution folder (satisfies `ArchitectureGuardTests.Solution_folders_collapse_leaf_project_segments`).
- [X] T003 Add a `ProjectReference` to `Elsa.Workflows.Publishing` from `src/Elsa/Workflows/Publishing/Api/Elsa.Workflows.Publishing.Api.csproj` (Api `DependsOn` the engine and references its types; Server pulls it transitively). Engine project builds green (0 errors).

**Checkpoint**: New engine project exists, compiles empty, and is in the solution.

---

## Phase 2: Foundational — THE SPLIT (blocking prerequisite)

**⚠️ CRITICAL**: The whole split lands here. No user-story validation can pass until this phase is complete (existing tests won't compile mid-move).

### Relocate the mediator contract to Core (FR-005, FR-009)

- [X] T004 Move `PublishWorkflow` record → `src/Elsa/Workflows/Publishing/Core/Requests/PublishWorkflow.cs`, namespace `Elsa.Workflows.Publishing.Core.Requests` (from `Api/Requests/PublishWorkflow.cs`).
- [X] T005 Move `PublishedWorkflowView` → `src/Elsa/Workflows/Publishing/Core/Models/PublishedWorkflowView.cs`, namespace `Elsa.Workflows.Publishing.Core.Models` (from `Api/Models/PublishedWorkflowView.cs`; it is Core-clean).
- [X] T006 Update the 2 production references to the new Core namespace: the endpoint alias + construction in `src/Elsa/Workflows/Publishing/Api/Endpoints/PublishWorkflow.cs:13,32`, and the handler signature (moved in T009). Update test `using`s that reference the old `Api.Requests`/`Api.Models` locations.

### Create the engine feature and move the engine into it (FR-001, FR-004)

- [X] T007 Create `src/Elsa/Workflows/Publishing/WorkflowsPublishingFeature.cs` — `[ShellFeature("WorkflowsPublishing")]`, a `public` non-sealed class implementing `IShellFeature` with `public virtual void ConfigureServices(IServiceCollection services)` (overridable per §2.23.3), `DependsOn = { "WorkflowsRuntimeTriggers", "Events" }`. No FastEndpoints/API base.
- [X] T008 Move only the **workflow-publish** handlers to `src/Elsa/Workflows/Publishing/Handlers/`: `PublishWorkflowRequestHandler`, `StartWorkflowTestRunRequestHandler`, `PublicationSlotLifecycleRequestHandlers`, `RunRuntimeRequirementPreflightRequestHandler`, and the `CollectExecutableCompilation` / `CollectExecutableNodeMetadata` event handlers. **Leave** `PublishActivityDraftRequestHandler` and `ActivityPublicationPreflightHandlers` in `Api/Handlers/` (activity-draft, authorization-coupled — Decision 3).
- [X] T009 Move the **auth-free** engine services from `Api/Services/` to `src/Elsa/Workflows/Publishing/Services/`: the compiler collaborator graph + `WorkflowExecutableCompiler`, template registries/compiler, projection reconciler/activator/preflight, publication policy resolver/preflight, in-memory publication/executable/snapshot/receipt stores, layout fallback + `DefaultActivityStructureService`, deletion guard, sidecar contexts, `WorkflowPublicationPreflightReader`, `PublicationSnapshotReviewService`. **Do NOT move** `ActivityDefinitionPublisher`, `ActivityDraftTestRunService`, `HttpContextActivityPublishingAuthorizationContext`, or `SourceOwnedActivityVersionPublisher` — they stay in `Api/Services/` (Decision 3/4).
- [X] T010 Move the workflow-publish exception types (`PublicationPreflightConflictException`, `PublicationActivationException`, `ExpressionPublicationValidationException`, `WorkflowExecutableCompilationException`) to `src/Elsa/Workflows/Publishing/Exceptions/` — they stay visible to the endpoint's ProblemDetails catch blocks via the Api→engine reference.
- [X] T011 In `WorkflowsPublishingFeature.ConfigureServices`, register every moved auth-free engine service (the ENGINE-classified subset of the old Api feature) plus `AddRequestHandlersFrom(GetType().Assembly)`, the two `AddEventHandler<Collect*>` registrations, and `TimeProvider.System`. Do NOT register `AddHttpContextAccessor`, any authorization context, endpoints, or API capabilities.

### Authorization stays in Api (Decision 3 — no engine changes)

- [X] T012 Verify (and keep) in the Api feature: `AddHttpContextAccessor()`, `IActivityPublishingAuthorizationContext → HttpContextActivityPublishingAuthorizationContext`, and the activity-draft services `ActivityDefinitionPublisher` / `ActivityDraftTestRunService` (+ `IActivityDraftTestRunStore`, cancellation policy) + `SourceOwnedActivityVersionPublisher`. Confirm no moved engine service references `IActivityPublishingAuthorizationContext` (engine builds without it). **No neutral default is introduced.**

### Slim the Api feature to transport + activity-draft (FR-002, FR-003, US3 core change)

- [X] T013 Edit `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs` — keep `: FastEndpointsFeatureBase` (unchanged base), set `DependsOn = { "WorkflowsPublishing", "ApiCapabilities" }`, and in `ConfigureServices` keep `base.ConfigureServices(services)` + the FastEndpoints endpoints + `AddApiCapability(PublishingApiCapabilities.StaticDeclaration)` + `AddApiCapabilitySource<ConversionProfilesCapabilitySource>()` + `AddHttpContextAccessor()` + the HttpContext authorization registration + the activity-draft service registrations (from T012). **Remove** every workflow-publish engine-service registration (now supplied by the engine feature via `DependsOn`).

### Repoint downstream consumers (Decision 5)

- [X] T014 [P] Change `DependsOn "WorkflowsPublishingApi"` → `"WorkflowsPublishing"` in `src/Elsa/Activities/Graph/Design/GraphActivitiesDesignFeature.cs:17`.
- [X] T015 [P] Change `DependsOn "WorkflowsPublishingApi"` → `"WorkflowsPublishing"` in `src/Elsa/Activities/DispatchWorkflow/Design/DispatchWorkflowDesignFeature.cs:18`.

**Checkpoint**: Solution compiles; the split is mechanically complete.

---

## Phase 3: User Story 1 — Compose the publish engine without endpoints (P1) 🎯

**Goal**: Prove the engine composes standalone with no HTTP publish endpoints.
**Independent Test**: `new WorkflowsPublishingFeature().ConfigureServices(services)` → resolve handler/compiler/stores; assert `IActivityPublishingAuthorizationContext` is absent; assert zero publish endpoints.

- [X] T016 [US1] Create engine registration test (§2.23.1) `tests/Elsa/Workflows/Publishing/Tests/WorkflowsPublishingFeatureTests.cs` (+ `Elsa.Workflows.Publishing.Tests.csproj` referencing the engine project): compose the engine feature alone; assert `IRequestHandler<PublishWorkflow, PublishedWorkflowView>`, `IWorkflowExecutableCompiler`, and the publication stores resolve; assert `IActivityPublishingAuthorizationContext` is **NOT** registered (engine is authorization-free); assert no `Elsa.Api.FastEndpoints` publish endpoint types are registered (SC-002/SC-003).
- [X] T017 [US1] Add the new test project to `Elsa.Server.slnx` (ArchitectureGuard).

**Checkpoint**: Engine-only composition proven headless.

---

## Phase 4: User Story 2 — Existing publishing API behaviour unchanged (P1)

**Goal**: The existing suite stays green; the endpoint surface is identical.
**Independent Test**: Run the existing publishing + architecture suites unchanged (assertions unmodified).

- [X] T018 [US2] Update file-path literals in `tests/Elsa/Architecture/GroundworkPersistenceLifetimeTests.cs:138-140` for `IPublicationProjectionPreparer`/`IPublicationActivator`/`PublicationSnapshotReviewService` to the engine feature file (subject/objective preserved — §2.21.1).
- [X] T019 [US2] Extend `tests/Elsa/Workflows/Publishing/Api/Tests/BridgeDependencyDirectionTests.cs:21` to assert the **engine** assembly also honours the forbidden-reference list; extend `tests/Elsa/Architecture/RuntimeExecutionSliceDependencyTests.cs:32` to cover the engine assembly's non-reference to `Runtime.Api`.
- [X] T020 [US2] Add an engine `ProjectReference` to `tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj` only if it references the handler/view types directly (else confirm the reference resolves transitively via the Api feature).
- [X] T021 [US2] Update the **wiring** of `WorkflowsPublishingApiFeatureTests` (§2.21.1, setup-only): its engine-service presence assertions currently rely on a direct `new WorkflowsPublishingApiFeature().ConfigureServices()` call, but engine services now arrive via `DependsOn`. Compose the engine feature alongside the Api feature in the arrange step (or move the engine-service assertions to T016's engine test). The `IActivityPublishingAuthorizationContext` (line 37) and activity-draft presence assertions stay satisfied by the Api feature directly. Preserve every assertion's subject/objective; change only setup.
- [X] T021b [US2] Run `dotnet test` for `Publishing/Api/Tests` and `Publishing/Persistence/Groundwork/Tests`; confirm `PublishWorkflowRequestHandlerTests`, `PublishWorkflowTriggerIndexingTests`, `PublishingGroundworkLifetimeTests`, and the re-wired `WorkflowsPublishingApiFeatureTests` pass (assertions unchanged) (SC-001, SC-004).

**Checkpoint**: Behaviour preservation verified.

---

## Phase 5: User Story 3 — The API feature carries only transport (P2)

**Goal**: Documented, legible layering — API = endpoints only.
**Independent Test**: Review Api `ConfigureServices` (base + HTTP override + endpoints + capabilities only); catalogs updated.

- [X] T022 [US3] Author `src/Elsa/Workflows/Publishing/EXTENSION_POINTS.md` + `README.md` for the engine feature (owns the `ExecutableCompilationCollecting`/`ExecutableNodeMetadataCollecting` events, `IExecutableCompilationSource`/`IExecutableNodeMetadataSource` contributor interfaces, and the template registries) per §2.22/§2.22.1.
- [X] T023 [US3] Update `src/Elsa/Workflows/Publishing/Api/EXTENSION_POINTS.md` to endpoints-only and add the engine's catalog row to the repo-root `EXTENSION_POINTS.md` index.
- [X] T024 [US3] Confirm (code review against SC-003) the Api `ConfigureServices` body registers nothing beyond `base`, the HTTP override, endpoints, and capabilities.

**Checkpoint**: Layering correct and documented.

---

## Phase 6: Polish & Cross-Cutting

- [X] T025 [P] Refresh the generated `docs/maps/domain-map.md` so Publishing's surface packages enumerate `Elsa.Workflows.Publishing`, `…Publishing.Api`, `…Publishing.Core` (the §E2.1 table defers enumeration to this map — do not hand-edit the constitution).
- [X] T026 [P] Refresh generated extension-point maps/index for the new engine feature.
- [X] T027 Record the MAJOR SemVer bump for the affected package(s) (relocated `PublishWorkflow`/`PublishedWorkflowView`) per §4.2 in the PR description.
- [ ] T028 Full `dotnet build Elsa.Server.slnx` + run the `quickstart.md` validation (engine-only compose, API-enabled endpoint smoke).

---

## Dependencies & Execution Order

- **Phase 1 (Setup)** → **Phase 2 (Foundational split)** is the hard gate: nothing else compiles until Phase 2 is complete.
- Within Phase 2: T004–T006 (contract move) and T007–T011 (engine move) are tightly coupled and largely sequential (shared files); T012 depends on T009 (services present); T013 depends on T007+T011 (engine feature exists); T014/T015 are independent `[P]` edits after T007.
- **Phase 3 (US1)**, **Phase 4 (US2)**, **Phase 5 (US3)** all depend on Phase 2. They are independent of each other and can proceed in parallel once the split lands.
- **Phase 6 (Polish)** depends on Phases 3–5.

### Parallel opportunities

- T014 + T015 (downstream repoints) — different files, run together.
- Once Phase 2 lands: US1 (T016–T017), US2 (T018–T021), US3 (T022–T024) can run in parallel.
- T025 + T026 (map refreshes) — parallel.

## Implementation Strategy

The MVP is the **atomic split**: Phase 1 + Phase 2 + Phase 4 (existing suite green) = a mergeable, behaviour-preserving refactor. US1 (T016–T017, the engine-only proof) is what makes the split *worth* doing (it's the PR2 enabler) and should land in the same PR. US3's docs/layering and Phase 6 polish complete the unit. Do not split this across PRs — the whole thing is one scope.

## Notes

- Moves (T004–T010) are file relocations + namespace edits, not rewrites — preserve behaviour exactly.
- There is **no genuinely-new code** — T012 only verifies authorization stays in Api; everything else is move + re-register.
- Commit after each coherent group; keep the working tree buildable at phase checkpoints.

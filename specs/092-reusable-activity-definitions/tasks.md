---

description: "Dependency-ordered implementation tasks for reusable activity definitions"
---

# Tasks: Reusable Activity Definitions

**Input**: Design documents from `/specs/092-reusable-activity-definitions/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Required. The specification defines a mandatory restart/recovery gate, API contracts, compatibility matrices, architecture guards, and migration fixtures. Test tasks precede their corresponding implementation tasks.

**Organization**: Shared substrate is completed first. User-story phases then preserve an independently testable outcome even where later stories extend the same backend seams.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it owns different files and does not depend on another incomplete task in the same phase.
- **[Story]**: Maps directly to the seven user stories in `spec.md`.
- Every task names its authoritative target file or directory.

## Phase 1: Setup and ratchets

**Purpose**: Establish project boundaries, solution membership, and fail-first architecture checks before behavior lands.

- [X] T001 Create `src/Elsa/Activities/Graph/Design/Elsa.Activities.Graph.Design.csproj`, `src/Elsa/Activities/Graph/Runtime/Elsa.Activities.Graph.Runtime.csproj`, `src/Elsa/Workflows/Publishing/Persistence/Groundwork/Elsa.Workflows.Publishing.Persistence.Groundwork.csproj`, `src/Elsa3/Activities/Design/Import/Persistence/Groundwork/Elsa3.Activities.Design.Import.Persistence.Groundwork.csproj`, and `tests/Elsa/Activities/Graph/Tests/Elsa.Activities.Graph.Tests.csproj` with only the cross-domain references allowed by `plan.md`
- [X] T002 Add the graph, Publishing Groundwork bridge, Elsa3 import Groundwork bridge, and graph test projects to `Elsa.Server.slnx` and wire their package versions in `Directory.Packages.props` only if new external dependencies are unavoidable
- [X] T003 [P] Add fail-first Runtime-to-Design/Publishing and legacy-surface guards for spec 092 in `tests/Elsa/Architecture/ReusableActivityArchitectureTests.cs`
- [X] T004 [P] Add graph feature extension-point skeletons and registration inventories in `src/Elsa/Activities/Graph/Design/EXTENSION_POINTS.md` and `src/Elsa/Activities/Graph/Runtime/EXTENSION_POINTS.md`
- [X] T005 Verify existing `.gitignore` and repository formatting/build conventions cover new .NET projects; record any necessary project-only changes in `.gitignore`

---

## Phase 2: Foundational contracts and durable identities

**Purpose**: Land the provider-neutral models, stable Runtime dispatch identity, persistence ports, and conformance fixtures that block every story.

**⚠️ CRITICAL**: No story implementation starts until stable provider/consumer identity and the Runtime dependency guard compile together.

- [X] T006 [P] Add public contract, provider manifest, authority, fork-origin, draft, diagnostic, diff, dependency, lifecycle, and upgrade models in `src/Elsa/Activities/Design/Core/Models/ReusableActivityModels.cs`
- [X] T007 [P] Add provider, registry, validation, version-diff, admission, and upgrade contracts in `src/Elsa/Activities/Design/Core/Contracts/ReusableActivityContracts.cs`
- [X] T008 Add Groundwork-targeted sibling draft, layout, validation, enriched publication, authority/head, and direct-edge entities in `src/Elsa/Activities/Design/Persistence/Core/Entities/ReusableActivityEntities.cs`; evolve provider-neutral read models without adding fields or migrations to `src/Elsa/Activities/Design/Persistence/EFCore/Configurations/ActivityDefinitionConfiguration.cs` or `ActivityDefinitionVersionConfiguration.cs`
- [X] T009 Add separate command/read/store ports for definitions, drafts, versions, layouts, validation, direct dependencies, and atomic publication in `src/Elsa/Activities/Design/Persistence/Core/Stores/ReusableActivityStores.cs` and `Contracts/ReusableActivityCommands.cs`
- [X] T010 [P] Add `RuntimeActivityDescriptor`, `RuntimeRequirement`, and activation-failure models in `src/Elsa/Activities/Runtime/Core/Models/RuntimeActivityDescriptor.cs` and `src/Elsa/Activities/Runtime/Core/Exceptions/ActivityResolutionException.cs`
- [X] T011 Replace CLR `DescriptorType` dispatch with `(ConsumerKey, SchemaVersion)` construction contracts and duplicate-registration rules in `src/Elsa/Activities/Runtime/Core/Contracts/IActivityConstructor.cs` and `src/Elsa/Activities/Runtime/Services/ActivityConstructorRegistry.cs`
- [X] T012 Migrate first-party activity constructors and executable-node construction callers from `DescriptorType` to stable consumer/schema keys across `src/Elsa/Activities/**/Constructors/*.cs`, `src/Elsa/Workflows/Publishing/Api/Services/ExecutableNodeCompiler.cs`, and `src/Elsa/Workflows/Runtime/Core/Models/ExecutableNode.cs`
- [X] T013 Update constructor registry, executable serialization, and representative first-party constructor tests for the stable descriptor contract in `tests/Elsa/Activities/Runtime/Tests/ActivityConstructorRegistryTests.cs`, `tests/Elsa/Workflows/Publishing/Api/Tests/ExecutableNodeCompilerTests.cs`, and affected `tests/Elsa/Activities/*/Tests/*.cs`
- [X] T014 [P] Add Runtime-owned executable activity template, placement origin, hierarchy layout, scope, and attempt-lineage models in `src/Elsa/Workflows/Runtime/Core/Models/ReusableActivityRuntimeModels.cs` and evolve `WorkflowExecutableSourceReference.cs`
- [X] T015 [P] Add executable-template, hierarchy-inspection, and source-reference read/write ports in `src/Elsa/Workflows/Runtime/Core/Contracts/ReusableActivityRuntimeStores.cs`
- [X] T016 Add Groundwork document definitions/indexes for activity drafts, layouts, validations, publication facts, direct edges, templates, and hierarchy projections in `src/Elsa/Activities/Design/Persistence/Groundwork/ActivitiesDesignStorageManifest.cs` and `src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs`, leaving EF schemas unchanged
- [X] T017 Add reusable in-memory conformance stores and fixtures in `tests/Elsa/Activities/Design/Tests/Fixtures/InMemoryReusableActivityStores.cs` and `tests/Elsa/Workflows/Runtime/Tests/Fixtures/InMemoryReusableActivityRuntimeStores.cs`
- [X] T018 Make T003 architecture guards pass for all foundational project references and stable wire identities in `tests/Elsa/Architecture/ReusableActivityArchitectureTests.cs`

**Checkpoint**: Stable Design provider and Runtime consumer contracts compile without Runtime references to Design or Publishing.

---

## Phase 3: User Story 1 — Author and publish a graph-backed activity (Priority: P1) 🎯 MVP

**Goal**: Create, revise, validate, and atomically publish one graph-backed definition/version/template through the Activity Catalog.

**Independent Test**: Create a definition and draft with one input/output/Done, validate it, publish `1.0.0`, and prove version, template, source reference, direct edges, and head are all visible together; force each gate to fail and prove zero partial state.

### Tests for User Story 1

- [X] T019 [P] [US1] Add fail-first definition/draft authoring API contract tests in `tests/Elsa/Activities/Design/Tests/Api/ActivityDefinitionAuthoringApiTests.cs`
- [X] T020 [P] [US1] Add fail-first optimistic revision, content-authority, and atomic draft-store tests in `tests/Elsa/Activities/Design/Tests/ReusableActivityDraftCommandTests.cs`
- [X] T021 [P] [US1] Add fail-first graph manifest validation, contract-fidelity, determinism, and migration tests in `tests/Elsa/Activities/Graph/Tests/GraphActivityProviderTests.cs`
- [X] T022 [P] [US1] Add fail-first atomic publication failpoint, cycle-path, SemVer, template-hash, and Source Reference tests in `tests/Elsa/Workflows/Publishing/Api/Tests/ActivityDefinitionPublicationTests.cs`
- [X] T023 [P] [US1] Add Groundwork definition/draft/layout/validation/version/edge atomicity tests in `tests/Elsa/Activities/Design/Persistence/Groundwork/Tests/GroundworkReusableActivityStoreTests.cs`

### Implementation for User Story 1

- [X] T024 [P] [US1] Implement graph manifest schema 1 and canonical serialization in `src/Elsa/Activities/Graph/Design/Models/ActivityGraphManifest.cs`
- [X] T025 [US1] Implement provider registry, deterministic validation ordering, and provider exception wrapping in `src/Elsa/Activities/Design/Core/Services/ActivityProviderRegistry.cs` and `ActivityDiagnosticOrderer.cs`
- [X] T026 [US1] Implement graph contract proposal, validation, compilation, measurement, and manifest migration in `src/Elsa/Activities/Graph/Design/Services/GraphActivityProvider.cs`
- [X] T027 [US1] Implement definition/draft create, clone, replace, discard, validate, and source-owned fork commands with optimistic revisions in `src/Elsa/Activities/Design/Api/Commands/ReusableActivityDraftCommands.cs`
- [X] T028 [US1] Implement definition/draft/version authoring request handlers and safe provider-payload projection in `src/Elsa/Activities/Design/Api/Handlers/ReusableActivityAuthoringHandlers.cs`
- [X] T029 [US1] Implement authoring/fork/draft/validate/version endpoints and views from `contracts/authoring-api.md` in `src/Elsa/Activities/Design/Api/Endpoints/ReusableActivityAuthoringEndpoints.cs` and `Models/ReusableActivityViews.cs`
- [X] T030 [US1] Implement exact dependency resolution, iterative cycle detection, deterministic template hashing, and admission evaluation in `src/Elsa/Workflows/Publishing/Api/Services/ActivityTemplateCompiler.cs`
- [X] T031 [US1] Implement expected-revision/head atomic publication coordination and structured rejection in `src/Elsa/Workflows/Publishing/Api/Services/ActivityDefinitionPublisher.cs`
- [X] T032 [US1] Implement Design-owned Groundwork stores in `src/Elsa/Activities/Design/Persistence/Groundwork/Services/GroundworkReusableActivityStores.cs` and the cross-domain atomic version/template/SourceReference/edge/head publication mutation in `src/Elsa/Workflows/Publishing/Persistence/Groundwork/Services/GroundworkActivityPublicationCommand.cs`
- [X] T033 [US1] Register Activity Design authoring, graph provider, Publishing coordinator, Groundwork stores, and API endpoints in `src/Elsa/Activities/Design/Api/ActivitiesDesignApiFeature.cs`, `src/Elsa/Activities/Graph/Design/GraphActivitiesDesignFeature.cs`, `src/Elsa/Activities/Design/Persistence/Groundwork/DependencyInjection/GroundworkActivitiesDesignStoreRegistration.cs`, and `src/Elsa/Workflows/Publishing/Persistence/Groundwork/PublishingGroundworkFeature.cs`

**Checkpoint**: US1 is independently green and exposes one immutable graph-backed activity version through the existing Activity Catalog.

---

## Phase 4: User Story 2 — Execute reusable behavior inside one workflow run (Priority: P1)

**Goal**: Place exact templates and execute them as ordinary composite activity scopes with durable suspension/recovery and no child workflow.

**Independent Test**: Place the same version twice, suspend inside one descendant, destroy the host, resume from Groundwork SQLite in a Runtime-only host, propagate one output once, and verify one workflow execution identity.

### Tests for User Story 2

- [X] T034 [P] [US2] Add fail-first repeated/nested placement, full-hash identity, subtree-stability, and deep iterative traversal tests in `tests/Elsa/Workflows/Publishing/Api/Tests/ActivityTemplatePlacementTests.cs`
- [X] T035 [P] [US2] Add fail-first graph entry/input isolation/default/null/capture and natural completion/output tests in `tests/Elsa/Activities/Graph/Tests/GraphActivityExecutionTests.cs`
- [X] T036 [P] [US2] Add fail-first native bookmark suspension and complete Runtime-only host restart gate in `tests/Elsa/Workflows/Publishing/Api/Tests/ActivityDraftTestRunTests.cs`, where the synthetic wrapper, graph publication, Runtime pipeline, and host-generation boundary can be exercised together
- [X] T037 [P] [US2] Add fail-first fault causation, cancellation/resume orderings, descendant cleanup, and fresh retry lineage tests in `tests/Elsa/Activities/Graph/Tests/GraphActivityRecoveryTests.cs`

### Implementation for User Story 2

- [X] T038 [US2] Implement canonical length-framed invocation origins and full SHA-256 node/resume/layout namespacing in `src/Elsa/Workflows/Publishing/Api/Services/ActivityTemplatePlacer.cs`
- [X] T039 [US2] Integrate exact activity-version template resolution/default compilation/placement into workflow compilation in `src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs` and `ExecutableNodeCompiler.cs`
- [X] T040 [US2] Persist and read content-addressed executable activity templates plus closed Runtime requirements in `src/Elsa/Workflows/Runtime/Services/InMemoryExecutableActivityTemplateStore.cs` and `src/Elsa/Persistence/Groundwork/Stores/GroundworkExecutableActivityTemplateStore.cs`
- [X] T041 [US2] Implement `GraphActivity` and its stable consumer/schema constructor in `src/Elsa/Activities/Graph/Runtime/Activities/GraphActivity.cs` and `Constructors/GraphActivityConstructor.cs`
- [X] T042 [US2] Implement nearest activity-execution scope capabilities, isolated durable input/local/output keys, and read-only input access in `src/Elsa/Activities/Graph/Runtime/Services/GraphActivityScope.cs`
- [X] T043 [US2] Fold graph entry input capture/local initialization/first-child intent into the existing activity checkpoint pipeline in `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`
- [X] T044 [US2] Fold graph completion/output capture/Done/outer terminalization/parent intent into one checkpoint in `src/Elsa/Activities/Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs` and `src/Elsa/Workflows/Runtime/Services/WorkflowCompleteActivitySchedulerWorkHandler.cs`
- [X] T045 [US2] Add execution-scope and attempt provenance to `src/Elsa/Workflows/Runtime/Core/Models/ActivityExecutionState.cs`, `ActivitySchedulingProvenance.cs`, and `RuntimeSchedulerWorkItem.cs`, plus Groundwork envelopes in `src/Elsa/Persistence/Groundwork/Stores/GroundworkActivityExecutionStateStore.cs`, `GroundworkActivityExecutionInspectionStore.cs`, and `GroundworkRuntimeCheckpointWriter.cs`
- [X] T046 [US2] Implement scoped descendant fault propagation with stable causal incident metadata in `src/Elsa/Activities/Graph/Runtime/Services/GraphActivityRecovery.cs` and `src/Elsa/Activities/Runtime/Services/ActivityFaultIncidentRecorder.cs`
- [X] T047 [US2] Add scope-cancellation command/payload, scheduling fence, provider-neutral bookmark/timer/work-queue cleanup ports, and one atomic cleanup/terminalization checkpoint in `src/Elsa/Workflows/Runtime/Core/Models/CancelActivityScopeCommand.cs`, `src/Elsa/Workflows/Runtime/Core/Contracts/IActivityScopeCleanupStore.cs`, and `src/Elsa/Workflows/Runtime/Services/WorkflowCancelActivityScopeSchedulerWorkHandler.cs`
- [X] T048 [US2] Add semantic graph-boundary retry command/handler that creates a fresh outer execution and descendants while reusing exact template/effective captured inputs and recording attempt lineage in `src/Elsa/Workflows/Runtime/Core/Models/RetryActivityBoundaryCommand.cs` and `src/Elsa/Workflows/Runtime/Services/WorkflowRetryActivityBoundarySchedulerWorkHandler.cs`
- [X] T049 [US2] Register the Runtime graph consumer without Design/Publishing references in `src/Elsa/Activities/Graph/Runtime/GraphActivitiesRuntimeFeature.cs` and compose it in `src/Apps/Elsa.Server/Program.cs`

**Checkpoint**: The mandatory suspend/destroy/restart/resume path is green with no Design stores and no child workflow identity.

---

## Phase 5: User Story 3 — Pin and upgrade versions explicitly (Priority: P1)

**Goal**: Explain compatibility and apply exact bottom-up draft upgrades without mutating published material.

**Independent Test**: Compare breaking versions, reject an insufficient bump, plan exact nested replacements, reject stale apply, and atomically update a selected dependency-closed draft set while old executables remain byte-identical.

### Tests for User Story 3

- [X] T050 [P] [US3] Add complete compatibility-matrix, deterministic ordering, safe projection, and provider-strengthening tests in `tests/Elsa/Activities/Design/Tests/ActivityVersionDiffTests.cs`
- [X] T051 [P] [US3] Add authoritative-direct versus derived dependency-page/cursor/watermark tests in `tests/Elsa/Activities/Design/Tests/ActivityDependencyQueryTests.cs`
- [X] T052 [P] [US3] Add bottom-up plan, dependency-closed selection, stale snapshot, atomic apply, and published-immutability tests in `tests/Elsa/Activities/Design/Tests/ActivityUpgradePlanTests.cs`

### Implementation for User Story 3

- [X] T053 [US3] Implement platform compatibility classification and provider-strengthened change merging in `src/Elsa/Activities/Design/Core/Services/ActivityVersionDiffer.cs`
- [X] T054 [US3] Add immutable-version and draft-preview diff handlers/endpoints in `src/Elsa/Activities/Design/Api/Endpoints/ActivityVersionDiffEndpoints.cs`
- [X] T055 [US3] Implement authoritative outbound and derived incoming/transitive dependency projection with bound cursors in `src/Elsa/Activities/Design/Api/Services/ActivityDependencyReader.cs`
- [X] T056 [US3] Add dependency route/read models from `contracts/dependencies-and-upgrades.md` in `src/Elsa/Activities/Design/Api/Endpoints/ActivityDependencyEndpoints.cs`
- [X] T057 [US3] Implement exact replacement discovery, bottom-up ordering, expected snapshots, and blocked publish handoffs in `src/Elsa/Activities/Design/Api/Services/ActivityUpgradePlanner.cs`
- [X] T058 [US3] Implement dependency-closed upgrade apply orchestration in `src/Elsa/Workflows/Publishing/Api/Commands/ApplyActivityUpgradePlanCommand.cs` without letting Activity Design own cross-workflow mutations
- [X] T059 [US3] Add upgrade-plan create/read/apply endpoints and views in `src/Elsa/Activities/Design/Api/Endpoints/ActivityUpgradeEndpoints.cs`
- [X] T060 [US3] Persist/rebuild reverse dependency projections in `src/Elsa/Activities/Design/Persistence/Groundwork/Services/GroundworkActivityDependencyProjection.cs` and atomically apply cross-activity/workflow upgrade plans in `src/Elsa/Workflows/Publishing/Persistence/Groundwork/Services/GroundworkActivityUpgradePlanStore.cs`

**Checkpoint**: Exact pinning remains invariant while compatible upgrades are explicit, reviewable, and atomic over mutable drafts only.

---

## Phase 6: User Story 4 — Inspect the complete execution hierarchy safely (Priority: P2)

**Goal**: Extend existing Runtime inspection with bounded descendant pages, attempts, aggregates, and executed-reference layout.

**Independent Test**: Inspect a nested loop/bookmark/retry run through small pages, click into nested boundaries, compare structure-only/value permissions, and prove current Design layout changes do not affect the old run.

### Tests for User Story 4

- [X] T061 [P] [US4] Add detail boundary/attempt/aggregate projection tests in `tests/Elsa/Workflows/Runtime/Tests/ActivityExecutionInspectionProjectionTests.cs`
- [X] T062 [P] [US4] Add fixed-watermark paging, cursor binding/expiry, 10,000-descendant, and nested-boundary tests in `tests/Elsa/Workflows/Runtime/Tests/ActivityExecutionHierarchyTests.cs`
- [X] T063 [P] [US4] Add executed-reference layout and structure/value authorization/redaction API tests in `tests/Elsa/Workflows/Runtime/Tests/ActivityExecutionLayoutInspectionTests.cs`

### Implementation for User Story 4

- [X] T064 [US4] Extend canonical activity execution detail with optional boundary and attempt views in `src/Elsa/Workflows/Runtime/Api/Models/WorkflowExecutionViews.cs` and its existing projector
- [X] T065 [US4] Implement committed descendant relation queries, iterative relative depth, aggregates, and snapshot cursors in `src/Elsa/Workflows/Runtime/Api/Services/ActivityExecutionHierarchyReader.cs`
- [X] T066 [US4] Add descendants request/handler/endpoint in `src/Elsa/Workflows/Runtime/Api/Endpoints/GetActivityExecutionDescendantsEndpoint.cs`
- [X] T067 [US4] Implement executed Source Reference boundary-layout selection and executable-id joins in `src/Elsa/Workflows/Runtime/Api/Services/ActivityExecutionLayoutReader.cs`
- [X] T068 [US4] Add boundary layout request/handler/endpoint in `src/Elsa/Workflows/Runtime/Api/Endpoints/GetActivityExecutionLayoutEndpoint.cs`
- [X] T069 [US4] Persist/query hierarchy fields and stable committed watermarks in `src/Elsa/Persistence/Groundwork/Stores/GroundworkActivityExecutionHierarchyStore.cs`

**Checkpoint**: Operators can lazily click through the complete historical execution graph without Design reads or unbounded responses.

---

## Phase 7: User Story 5 — Add implementation providers without changing Runtime (Priority: P2)

**Goal**: Prove stable provider/consumer extensibility, preflight, and non-retryable activation incidents.

**Independent Test**: Register a test provider and Runtime consumer entirely through public seams; compile deterministically; then remove the consumer and verify preflight plus activation incident behavior without changing universal dispatch.

### Tests for User Story 5

- [X] T070 [P] [US5] Add second-provider conformance and duplicate provider/consumer key tests in `tests/Elsa/Activities/Graph/Tests/ActivityProviderConformanceTests.cs`
- [X] T071 [P] [US5] Add Runtime requirement preflight and artifact-activation incident tests in `tests/Elsa/Workflows/Publishing/Api/Tests/RuntimeRequirementPreflightTests.cs`

### Implementation for User Story 5

- [X] T072 [US5] Complete provider registry startup contributions and duplicate-key failure in `src/Elsa/Activities/Design/Core/Services/ActivityProviderRegistry.cs`
- [X] T073 [US5] Record exact Runtime requirements on activity templates and workflow artifacts in `src/Elsa/Workflows/Runtime/Core/Models/RuntimeRequirement.cs` and `WorkflowExecutable.cs`
- [X] T074 [US5] Implement retained-artifact Runtime requirement preflight in `src/Elsa/Workflows/Publishing/Api/Services/RuntimeRequirementPreflight.cs`
- [X] T075 [US5] Add `/publishing/preflight` request/view/endpoint in `src/Elsa/Workflows/Publishing/Api/Endpoints/RuntimeRequirementPreflightEndpoint.cs`
- [X] T076 [US5] Classify unresolved consumers as recoverable deployment incidents outside ordinary activity retry in `src/Elsa/Workflows/Runtime/Services/ActivityActivationFailureHandler.cs`

**Checkpoint**: A second provider can use stable seams and a missing Runtime consumer is detectable before dispatch and recoverable after deployment correction.

---

## Phase 8: User Story 6 — Work safely with drafts and lifecycle policy (Priority: P2)

**Goal**: Complete parallel-draft, provider migration, fork, lifecycle, test-run, and reference-lifetime behavior.

**Independent Test**: Publish one of two drafts, preserve the stale draft, fork source-owned content, migrate a Design draft, retire/restore/revoke versions, and run a draft through an expiring synthetic wrapper reference.

### Tests for User Story 6

- [X] T077 [P] [US6] Add parallel-draft/head-conflict, clone, source-authority fork, and provider migration API tests in `tests/Elsa/Activities/Design/Tests/ActivityDraftLifecycleTests.cs`
- [X] T078 [P] [US6] Add retire/restore/revoke and retained-parent executable tests in `tests/Elsa/Activities/Design/Tests/ActivityVersionLifecycleTests.cs`
- [X] T079 [P] [US6] Add synthetic-wrapper draft test-run, suspension, expiry, hash reuse, and reference GC tests in `tests/Elsa/Workflows/Publishing/Api/Tests/ActivityDraftTestRunTests.cs`

### Implementation for User Story 6

- [X] T080 [US6] Complete exact clone, independent source-owned fork provenance, and provider migration commands in `src/Elsa/Activities/Design/Api/Commands/ReusableActivityDraftCommands.cs`
- [X] T081 [US6] Implement optimistic retire/restore/revoke version commands and selection policy in `src/Elsa/Activities/Design/Api/Commands/ActivityVersionLifecycleCommands.cs`
- [X] T082 [US6] Add lifecycle endpoints and stable lifecycle conflict mappings in `src/Elsa/Activities/Design/Api/Endpoints/ActivityVersionLifecycleEndpoints.cs`
- [X] T083 [US6] Implement synthetic wrapper compilation and expiring Source Reference creation in `src/Elsa/Workflows/Publishing/Api/Services/ActivityDraftTestRunService.cs`
- [X] T084 [US6] Add draft test-run request/view/endpoint and normal Runtime dispatch in `src/Elsa/Workflows/Publishing/Api/Endpoints/ActivityDraftTestRunEndpoint.cs`
- [X] T084a [US6] Add durable exact-revision Test Run receipts, same-key dispatch idempotency, status lookup by identity/key, Runtime reconciliation, and explicit expiry/evidence facts
- [X] T084b [US6] Add policy-advertised idempotent cancellation plus full-host accepted, completion, fault, cancellation, ambiguous acknowledgement, authorization, and expiry coverage
- [X] T085 [US6] Extend reference-derived cleanup to activity-template and test-run references in `src/Elsa/Workflows/Runtime/Services/WorkflowExecutableReferenceGarbageCollector.cs`
- [X] T086 [US6] Persist version lifecycle, fork provenance, migrated drafts, and test-run references in `src/Elsa/Activities/Design/Persistence/Groundwork/Services/GroundworkReusableActivityStores.cs`
- [X] T087 [US6] Map every authoring/publication/lifecycle failure to shared RFC 7807 plus ordered safe diagnostics in `src/Elsa/Api/FastEndpoints/Configurators/ProblemDetailsFastEndpointConfigurator.cs` and `src/Elsa/Activities/Design/Api/Models/ActivityProblemDetails.cs`

**Checkpoint**: Draft and lifecycle operations preserve immutable exact execution material and one content authority per lineage.

---

## Phase 9: User Story 7 — Migrate Elsa 3 reusable workflows deliberately (Priority: P3)

**Goal**: Analyze and atomically convert Elsa 3 reusable workflow collections into activity definitions plus direct-start wrapper workflows.

**Independent Test**: Analyze a collection with two consumers, direct start, missing target, unsupported trigger, and cycle; apply a valid closure twice with deterministic/idempotent results and zero writes for invalid closures.

### Tests for User Story 7

- [X] T088 [P] [US7] Add collection plan fixtures for reusable references, direct starts, missing targets, unsupported triggers, and complete cycle paths in `tests/Elsa3/Mapping/Tests/ReusableActivityCollectionImportTests.cs`
- [X] T089 [P] [US7] Add atomic selected-closure, deterministic identity, exact rewrite, wrapper, and idempotent apply tests in `tests/Elsa3/Mapping/Tests/ReusableActivityCollectionApplyTests.cs`

### Implementation for User Story 7

- [X] T090 [US7] Add reusable-activity collection analysis/apply contracts and models in `src/Elsa3/Activities/Design/Import/Contracts/IReusableActivityCollectionImporter.cs` and `Models/ReusableActivityImportPlan.cs`
- [X] T091 [US7] Implement iterative collection graph analysis, deterministic ids, exact rewrites, missing/unsupported diagnostics, and cycle paths in `src/Elsa3/Activities/Design/Import/Services/ReusableActivityCollectionAnalyzer.cs`
- [X] T092 [US7] Implement provider-neutral selected-closure application orchestration in `src/Elsa3/Activities/Design/Import/Services/ReusableActivityCollectionImporter.cs` and one cross-domain Groundwork commit in `src/Elsa3/Activities/Design/Import/Persistence/Groundwork/Services/GroundworkReusableActivityImportCommand.cs`
- [X] T093 [US7] Adapt `src/Elsa3/Mapping/Services/Elsa3WorkflowDefinitionImporter.cs` to consume collection plans without silently converting recursion to separate-workflow execution
- [X] T094 [US7] Register importer capabilities and document them in `src/Elsa3/Activities/Design/Import/Elsa3ImportActivitiesFeature.cs` and `EXTENSION_POINTS.md`

**Checkpoint**: Elsa 3 is the only compatibility path and invalid collection closures cannot partially mutate Elsa 4 state.

---

## Phase 10: Clean break, documentation, and release gates

**Purpose**: Remove the competing Foundation model, ratchet terminology/boundaries, and prove the complete specification.

- [X] T095 Migrate still-relevant composition assertions into `tests/Elsa/Activities/Graph/Tests/LegacyCompositionReplacementTests.cs` and explicit `ExecuteWorkflow` preservation coverage, then remove `src/Elsa/Activities/Composition/Design/`, `src/Elsa/Activities/Composition/Runtime/`, and the obsolete composition test project plus their solution/server registrations, `src/Elsa/Workflows/Design/Core/Models/WorkflowActivityOptions.cs`, and Elsa 4 `UsableAsActivity` projections while preserving Elsa 3 import detection and `src/Elsa/Workflows/Runtime/Api/Requests/ExecuteWorkflow.cs`
- [X] T096 Remove CLR `DescriptorType` from universal Core/API/Runtime dispatch contracts in `src/Elsa/Activities/Design/Core/Contracts/IActivityDefinitionVersion.cs`, `Models/ActivityDefinitionVersionModel.cs`, `src/Elsa/Activities/Design/Api/Commands/AddVersion.cs`, and related reconciliation projections; isolate any compile-only legacy EF column without using it or adding an EF migration, and remove EF Activity Design from default server composition if necessary
- [X] T097 [P] Update affected extension catalogs, glossary terminology, architecture maps inputs, and program-goal evidence in `src/Elsa/Activities/Runtime/EXTENSION_POINTS.md`, `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`, `docs/glossary/elsa.md`, and `docs/program-goals/runtime-execution-seam.md`
- [X] T098 Run the mandatory Groundwork SQLite suspend/destroy/Runtime-only restart/resume/inspect gate from `specs/092-reusable-activity-definitions/quickstart.md` and record evidence in that file
- [X] T099 Run all focused Design, Graph, Runtime, Publishing, Groundwork, Elsa3, and Architecture test projects listed in `specs/092-reusable-activity-definitions/plan.md`
- [X] T100 Run `dotnet build Elsa.Server.slnx -c Release`, repository formatting/analyzers, `git diff --check`, and legacy/boundary searches; fix every regression in its owning source file
- [X] T101 Perform requirement-by-requirement completion audit for FR-001–FR-062 and SC-001–SC-012 and record authoritative evidence in `specs/092-reusable-activity-definitions/checklists/implementation-audit.md`
- [X] T102 Mark every completed task `[X]`, run the formal Speckit cross-artifact analysis, resolve all HIGH/CRITICAL findings in `specs/092-reusable-activity-definitions/tasks.md`, and commit the complete implementation work unit

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1**: starts from committed spec/plan baseline `512c3df0`.
- **Phase 2**: depends on T001–T005 and blocks all story behavior.
- **US1**: depends on Phase 2; establishes definitions, graph provider, and publication.
- **US2**: depends on US1's exact templates and publication seam.
- **US3**: depends on US1 versions/direct edges; can proceed in parallel with late US2 Runtime work once those contracts are stable.
- **US4**: depends on US2 execution-scope/attempt facts.
- **US5**: depends on Phase 2 stable registries and US1 template requirements; can proceed alongside US3/US4.
- **US6**: depends on US1 authoring/publication and US2 normal Runtime dispatch.
- **US7**: depends on US1 commands and exact-version model; can proceed alongside US4–US6 after those commands stabilize.
- **Phase 10**: depends on every story; legacy removal occurs only after replacement tests are green.

### User-story dependency graph

```text
Setup -> Foundation -> US1 -> US2 -> US4
                         |      |
                         |      +-> US6
                         +-> US3
                         +-> US5
                         +-> US7
US1 + US2 + US3 + US4 + US5 + US6 + US7 -> Clean break and release gates
```

### Within each story

- Write and run fail-first tests before implementation.
- Land domain models/ports before services, services before endpoints, and durable adapters before restart claims.
- Do not mark a task complete from compilation alone when its acceptance criterion requires runtime, persistence, or API evidence.
- The Runtime-to-Design/Publishing guard and published-artifact immutability tests run at every story checkpoint.

## Parallel execution examples

### Foundation

```text
Agent A: T006–T009 Design models and stores
Agent B: T010–T013 Runtime descriptor migration
Agent C: T014–T017 Runtime/template models and Groundwork manifests
Lead: integrate and prove T018 architecture guards
```

### After US1 contracts stabilize

```text
Agent A: US2 placement and Graph Runtime
Agent B: US3 diff/dependency/upgrade read models
Agent C: US5 preflight/provider conformance, then US7 migration
Lead: integration, US4/US6 seam review, and release-gate QA
```

## Implementation strategy

### Smallest safe vertical slice

1. Complete Setup + Foundation.
2. Complete US1 publication.
3. Complete US2 through the full restart/resume gate.
4. Complete US4 minimal hierarchy/layout read.
5. Remove the old workflow-as-activity path only after this replacement slice is green.

This is the minimum behavior that proves the concept; it is not permission to omit US3, US5, US6, or US7 from the end-to-end objective.

### Incremental integration

1. Commit task generation separately.
2. Commit each green logical implementation slice while keeping the branch buildable.
3. Rebase delegated work through the shared worktree only after root review.
4. Keep published material immutable and Runtime artifact-only at every intermediate commit.
5. Finish only when T101 proves every numbered requirement and success criterion with current-state evidence.

## Notes

- New EF schema/migrations are forbidden; Groundwork plus in-memory conformance stores are the only new persistence implementations.
- Exact ids are always explicit; no implementation may introduce a `latest` resolver.
- Foundation supplies measurements and replaceable admission policy, not arbitrary graph limits.
- No task may delete an existing behavior test merely to make the new model green.
- The constitutions are still provisional; the plan treats their current Runtime/Design and artifact-only gates as binding.

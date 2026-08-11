# Tasks: Deterministic and Bounded Workflow Dispatch

**Input**: Design documents from `/specs/097-dispatch-dependency-hardening/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required by FR-027 and the repository constitution. Write the listed tests first and observe the relevant failure before implementation.

**Organization**: Tasks are grouped by user story and remain bounded to DispatchWorkflow dependency/input/retention/depth hardening.

## Phase 1: Setup and Characterization

**Purpose**: Establish a stable #676 baseline and reusable fixtures before changing artifact wire behavior.

- [x] T001 Run the focused #676 baseline commands and record any pre-existing failures in specs/097-dispatch-dependency-hardening/quickstart.md
- [x] T002 Verify generated-map freshness against docs/maps/manifest.json and refresh only stale relevant map layers with tools/maps/generate-maps.sh
- [x] T003 [P] Add reusable executable/input/dependency builders without changing behavior in tests/Elsa/Workflows/Publishing/Api/Tests/Fakes.cs
- [x] T004 [P] Add reusable dispatch parent/child/start builders without changing behavior in tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowRuntimeTestFixture.cs
- [x] T005 [P] Add reusable retention graph builders without changing behavior in tests/Elsa/Workflows/Runtime/Tests/WorkflowExecutableReferenceGarbageCollectorTests.cs

---

## Phase 2: Foundational Artifact and Contribution Contracts

**Purpose**: Add the shared immutable contracts required by every story.

**⚠️ CRITICAL**: Complete this phase before story implementation.

- [x] T006 Add immutable direct dependency, versioned declared-input, and optional tenant-scoped source-reference models in src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutableDependency.cs, src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutableInputContract.cs, and src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutableSourceReference.cs
- [x] T007 Extend WorkflowExecutable with compatibility-safe input-contract/dependency snapshots and invariants in src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutable.cs
- [x] T008 [P] Add typed retained-dependency ID/hash/node provenance and additive compatibility constructors in src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutionStartDispatch.cs
- [x] T009 [P] Add start-policy context/decision and document the single-implementation replacement contract in src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutableStartPolicy.cs and src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutableStartDecision.cs
- [x] T010 Replace metadata-only fan-in contracts with tenant-aware compilation source/context/contribution contracts in src/Elsa/Workflows/Publishing/Core/Contracts/IExecutableCompilationSource.cs, src/Elsa/Workflows/Publishing/Core/Models/ExecutableCompilationContribution.cs, and src/Elsa/Workflows/Publishing/Core/Models/WorkflowExecutableCompileModels.cs
- [x] T011 Add the named Sequential compilation contribution event in src/Elsa/Workflows/Publishing/Core/Events/ExecutableCompilationCollecting.cs
- [x] T012 Add the single Publishing-owned aggregation handler with deterministic conflict validation in src/Elsa/Workflows/Publishing/Api/Handlers/CollectExecutableCompilation.cs
- [x] T013 Wire Elsa Events and the aggregation handler/source seam in src/Elsa/Workflows/Publishing/Api/Elsa.Workflows.Publishing.Api.csproj and src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs
- [x] T014 Update public API/serialization compatibility assertions for additive models, legacy read/run defaults, and legacy strict-target rejection in tests/Elsa/Workflows/Runtime/Tests/RuntimePublicApiCompatibilityTests.cs and tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerGoldenTests.cs

**Checkpoint**: New artifact and contribution contracts compile; old artifacts/start requests remain readable.

---

## Phase 3: User Story 1 - Publish a Validated Deterministic Dispatch (Priority: P1) 🎯 MVP

**Goal**: Publication pins one exact accessible live child, validates its declared inputs, and makes child behavior part of the parent identity.

**Independent Test**: Alter child liveness/access/input declarations or child behavior between authoring and parent publication; publication either produces one exact canonical dependency/hash or fails before activation with safe diagnostics.

### Tests for User Story 1

- [x] T015 [P] [US1] Add input-contract projection, canonical ordering, legacy-null, and hash golden tests in tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerGoldenTests.cs
- [x] T016 [P] [US1] Add dependency ordering, duplicate-node, diamond-equivalence, and transitive child-hash propagation tests in tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs
- [x] T017 [P] [US1] Add stale/missing/unpublished/ambiguous/inconsistent, cross-tenant, and resolution-then-replacement exact-pin tests in tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowDesignTests.cs
- [x] T018 [P] [US1] Add legacy-target, literal unknown/blank/duplicate/missing-required/incompatible/unknown-alias input tests plus declared runtime-like-name isolation in tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowDesignTests.cs
- [x] T019 [P] [US1] Add dynamic input validation, literal-default materialization, raw-value redaction, and runtime-channel isolation tests in tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs
- [x] T020 [P] [US1] Add feature wiring tests for the compile source, event handler, and input validator in tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowDesignFeatureTests.cs and tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowsPublishingApiFeatureTests.cs

### Implementation for User Story 1

- [x] T021 [US1] Carry publication tenant context and project a version-1 runtime input contract from WorkflowDefinitionState while keeping Runtime Design-free in src/Elsa/Workflows/Publishing/Api/Handlers/PublishWorkflowRequestHandler.cs and src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs
- [x] T022 [US1] Extend the canonical hasher with declared inputs and direct dependency/node bindings while excluding publication facts in src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableHasher.cs
- [x] T023 [US1] Publish/read the Sequential compile event and assemble validated dependencies before final hashing in src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs
- [x] T024 [US1] Implement reusable shared-TypeReference validation, unknown-alias failure, literal-default materialization, and safe findings in src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutableInputValidator.cs, src/Elsa/Workflows/Runtime/Services/WorkflowExecutableInputValidator.cs, and src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutableInputValidation.cs
- [x] T025 [US1] Register the default input validator and cover its feature wiring in src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs and tests/Elsa/Workflows/Runtime/Tests/RuntimeCoreCompositionRootTests.cs
- [x] T026 [US1] Evolve DispatchPinSource into a compilation source that revalidates same-tenant live Published upgraded children, validates literal inputs, and contributes exact dependency/node claims in src/Elsa/Activities/DispatchWorkflow/Design/Services/DispatchPinSource.cs
- [x] T027 [US1] Register the DispatchWorkflow compilation source and replace the superseded metadata-source registration in src/Elsa/Activities/DispatchWorkflow/Design/DispatchWorkflowDesignFeature.cs
- [x] T028 [US1] Validate realized dynamic input maps before staging dispatch responsibility and redact rejected values in src/Elsa/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs
- [x] T029 [US1] Update executable inspection views to expose declared inputs and direct dependencies without publication facts in src/Elsa/Workflows/Runtime/Api/Models/WorkflowExecutableInspectionViews.cs and src/Elsa/Workflows/Runtime/Api/Services/WorkflowExecutableInspector.cs

**Checkpoint**: US1 passes independently; a successful parent artifact is deterministic and every knowable invalid target/input fails before activation.

---

## Phase 4: User Story 2 - Keep Pinned Child Behavior Executable (Priority: P1)

**Goal**: Retained parents execute their original child after child replacement/unpublication; dependency closures remain retained; retired parents and host policy reject future starts without mutating artifacts.

**Independent Test**: Publish parent+child, replace/unpublish the child, start the retained parent successfully, then remove roots and prove shared/transitive closure collection; separately prove parent retirement and explicit policy denial create no execution state.

### Tests for User Story 2

- [x] T030 [P] [US2] Add exact retained parent ID/hash/node/child authority success, mismatch, and persisted dependency-provenance tests in tests/Elsa/Workflows/Runtime/Tests/RuntimeWorkflowExecutionStartDispatchTests.cs
- [x] T031 [P] [US2] Add allow/deny policy ordering, classifiable reason, zero-actor/state, and unchanged-artifact tests in tests/Elsa/Workflows/Runtime/Tests/RuntimeWorkflowExecutionStartDispatchTests.cs
- [x] T032 [P] [US2] Add direct/transitive/shared/diamond/final-root collection tests in tests/Elsa/Workflows/Runtime/Tests/WorkflowExecutableReferenceGarbageCollectorTests.cs
- [x] T033 [P] [US2] Add closure lease ordering, partial-acquisition release, concurrent root-vs-delete, and final recheck tests in tests/Elsa/Workflows/Runtime/Tests/WorkflowExecutableReferenceGarbageCollectorConcurrencyTests.cs
- [x] T034 [P] [US2] Add Groundwork executable dependency round-trip and closure lease fencing/recovery tests in tests/Elsa/Persistence/Groundwork/Tests/GroundworkWorkflowExecutableStoreTests.cs and tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeCheckpointRootWriteLeaseTests.cs
- [x] T035 [P] [US2] Add end-to-end original-child execution after replacement/unpublication and retired-parent rejection tests in tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs

### Implementation for User Story 2

- [x] T036 [US2] Authorize retained dependency starts by loading the exact parent edge while preserving ordinary live-reference gates in src/Elsa/Workflows/Runtime/Services/WorkflowStartDispatcher.cs
- [x] T037 [US2] Evaluate the replacement start policy after authority/input/depth validation and before actor lookup in src/Elsa/Workflows/Runtime/Services/WorkflowStartDispatcher.cs
- [x] T038 [US2] Register the default allow policy and detect replacement conflicts in src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs
- [x] T039 [US2] Remove historical-child live-reference dependence and send typed retained authority from src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ChildStartExecutor.cs
- [x] T040 [US2] Add deterministic dependency-closure traversal with missing/cycle/hash failure modes in src/Elsa/Workflows/Runtime/Services/WorkflowExecutableDependencyGraph.cs
- [x] T041 [US2] Add an additive closure-wide ExecuteAsync overload that leases sorted distinct artifacts and safely releases partial acquisitions in src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutableRootWriteLeaseManager.cs and src/Elsa/Workflows/Runtime/Services/WorkflowExecutableRootWriteLeaseManager.cs
- [x] T042 [US2] Use closure-wide leases for publication source-root and execution-state root creation in src/Elsa/Workflows/Publishing/Api/Handlers/PublishWorkflowRequestHandler.cs and src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs
- [x] T043 [US2] Compute live source/execution protected closures and repeat reachability under deletion guards in src/Elsa/Workflows/Runtime/Services/WorkflowExecutableReferenceGarbageCollector.cs
- [x] T044 [US2] Persist and fence the expanded immutable artifact/lease behavior in src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowExecutableStore.cs

**Checkpoint**: US2 passes independently; retained dependencies survive source replacement/unpublication and become collectable only after the final root.

---

## Phase 5: User Story 3 - Bound Recursive Dispatch Safely (Priority: P1)

**Goal**: Reject exact artifact cycles at publication and bound legal version-skewed/indirect dispatch chains with durable replay-stable depth.

**Independent Test**: Exact direct/transitive artifact cycles fail with deterministic paths; newer-to-older same-definition dispatch publishes; depths 1–32 succeed and attempted 33 fails before child materialization under defaults.

### Tests for User Story 3

- [x] T045 [P] [US3] Add malformed stored full-ID/hash cycle, candidate recurrence defense, truncated-ID/hash-mismatch, and same-definition different-artifact tests in tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs
- [x] T046 [P] [US3] Add start request/command/checkpoint/state JSON compatibility and root-zero tests in tests/Elsa/Workflows/Runtime/Tests/WorkflowStartLineageTests.cs and tests/Elsa/Workflows/Runtime/Tests/RuntimeCheckpointSerializationTests.cs
- [x] T047 [P] [US3] Add default/custom/invalid maximum depth and corrupt retained-start payload tests in tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs and tests/Elsa/Activities/DispatchWorkflow/Tests/ChildStartExecutorTests.cs
- [x] T048 [P] [US3] Add end-to-end depths 1–32, rejected 33, version-skew chain, and replay-no-inflation tests in tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs

### Implementation for User Story 3

- [x] T049 [US3] Validate child graphs by full artifact ID/hash and reject deterministic malformed/recurrent identity paths after candidate computation in src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs and src/Elsa/Workflows/Runtime/Services/WorkflowExecutableDependencyGraph.cs
- [x] T050 [US3] Add positive configurable MaxNestingDepth defaulting to 32 in src/Elsa/Activities/DispatchWorkflow/Runtime/Configuration/DispatchWorkflowOptions.cs and src/Elsa/Activities/DispatchWorkflow/Runtime/DispatchWorkflowRuntimeFeature.cs
- [x] T051 [US3] Thread DispatchNestingDepth compatibly through start request/command/checkpoint/state models in src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutionStartDispatch.cs, src/Elsa/Workflows/Runtime/Core/Models/RuntimeCheckpointCommandPayload.cs, and src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutionState.cs
- [x] T052 [US3] Persist one computed child depth through dispatch record/start payload and checkpoint staging in src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchRecord.cs and src/Elsa/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs
- [x] T053 [US3] Recheck stable depth without incrementing during outbox delivery and start dispatch in src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ChildStartExecutor.cs and src/Elsa/Workflows/Runtime/Services/WorkflowStartDispatcher.cs

**Checkpoint**: US3 passes independently; exact cycles cannot publish and all legal recursive shapes are bounded by typed durable depth.

---

## Phase 6: Documentation, Architecture, and Full Verification

**Purpose**: Reconcile public contracts, architectural decisions, maps, and complete quality gates.

- [x] T054 [P] Amend behavioral hash/dependency identity decisions in docs/adr/0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md
- [x] T055 [P] Amend transitive reachability/closure-lease decisions in docs/adr/0040-one-artifact-store-with-reference-derived-lifetime.md
- [x] T056 [P] Update handler/source/policy inventories and known implementations in src/Elsa/Workflows/Publishing/Api/EXTENSION_POINTS.md, src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md, src/Elsa/Activities/DispatchWorkflow/Design/README.md, and src/Elsa/Activities/DispatchWorkflow/Runtime/README.md
- [x] T057 [P] Add architecture guards excluding Runtime→Design, WorkflowDefinitionActivity, Studio, broker/MassTransit, later lifecycle slices, and distributed placement in tests/Elsa/Architecture/DispatchWorkflowArchitectureTests.cs
- [x] T058 Refresh relevant generated maps and review drift findings via tools/maps/generate-maps.sh, tools/maps/generate-domain-map.sh, tools/maps/generate-extension-point-map.sh, tools/maps/generate-architecture-reference-map.sh, and tools/maps/generate-feature-dependency-map.sh
- [x] T059 Run every focused command and expected outcome in specs/097-dispatch-dependency-hardening/quickstart.md
- [x] T060 Run full Elsa.Server.slnx restore/build, affected full test projects, git diff --check, and map-manifest review; record verified counts in specs/097-dispatch-dependency-hardening/tasks.md

### Verification record (2026-07-16)

- Restore: `Elsa.Server.slnx` up to date; existing Architecture `NU1510` warning only.
- Build: succeeded with 0 errors and 53 pre-existing Groundwork/Architecture warnings; no warning points to a #677-changed production file.
- Publishing API: 197 passed.
- Workflows Runtime: 994 passed.
- DispatchWorkflow: 56 passed.
- Groundwork: 299 passed, including v2→v3 executable/source-reference and v3→v4 execution-state migrations.
- Architecture: 204 passed.
- Affected full-test total: 1,750 passed, 0 failed, 0 skipped.
- Maps: all five generators completed; manifest counts are 135 source projects, 67 test projects, 91 feature classes, and 113 specs. Existing deferred `Elsa.Workflows.Runtime.JavaScript` → `Elsa.Workflows.Design.Core` remains the only runtime-to-design review signal; no new DispatchWorkflow boundary drift was reported.
- `git diff --check`: clean.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: starts immediately.
- **Foundational (Phase 2)**: depends on setup and blocks all stories.
- **US1 (Phase 3)**: starts after foundation and establishes compiler/input behavior.
- **US2 (Phase 4)**: starts after foundation; tasks T039/T042 consume US1's dependency/node binding behavior.
- **US3 (Phase 5)**: starts after foundation; cycle publication integration T049 consumes US1's compiler contribution, while depth work can proceed independently.
- **Polish (Phase 6)**: follows all selected stories.

### User Story Dependencies

- **US1**: no other story dependency; suggested MVP.
- **US2**: dependency model is foundational; end-to-end retained child test expects US1's exact binding.
- **US3**: depth is independently testable after foundation; exact-cycle publishing expects US1's dependency assembly.

### Parallel Opportunities

- T003–T005 can run in parallel.
- T008–T009 and T011 can run in parallel after T006/T007 boundaries are understood.
- US1 tests T015–T020 can be written in parallel before implementation.
- US2 tests T030–T035 can be written in parallel; GC/lease work T040–T044 is separable from start-policy work T036–T039 until integration.
- US3 tests T045–T048 can be written in parallel; option/depth model work T050–T052 can proceed alongside cycle work T049.
- Documentation/architecture tasks T054–T057 can run in parallel after APIs stabilize.

## Parallel Examples

### User Story 1

```text
Worker A: T015–T016 compiler/hash/input contract tests
Worker B: T017–T018 DispatchWorkflow design validation tests
Worker C: T019–T020 runtime validation and feature wiring tests
```

### User Story 2

```text
Worker A: T030–T031 retained authority/start policy
Worker B: T032–T033 in-memory closure retention and races
Worker C: T034–T035 Groundwork and end-to-end replacement scenarios
```

### User Story 3

```text
Worker A: T045/T049 exact-artifact graph validation
Worker B: T046/T051 start/checkpoint/state depth compatibility
Worker C: T047–T048/T050/T052–T053 runtime boundary and replay behavior
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational phases.
2. Complete US1.
3. Run Publishing and DispatchWorkflow focused suites.
4. Review deterministic dependency/input behavior before retention and recursion expansion.

### Incremental Delivery

1. US1: deterministic validated parent publication.
2. US2: retained exact child plus safe lifetime and denial.
3. US3: exact-cycle rejection and runtime depth bound.
4. Cross-cutting docs, maps, full verification, one local #677 commit.

## Notes

- Keep WorkflowDefinitionActivity and Studio work out of every phase.
- Do not add an activity-level broker selector or MassTransit dependency.
- Keep #678–#683 lifecycle/durability/distribution behavior out except where explicitly named as an exclusion test.
- Preserve existing test subjects/objectives; do not remove tests without recorded architect approval.
- Mark tasks complete only after implementation and the task's focused tests pass.

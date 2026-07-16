# Tasks: Role-Owned Workflow Value Flow

**Input**: Design documents from `/specs/095-value-flow-redesign/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, and
`test-migration-ledger.md`

**Tests**: Required. Within every story, add or adapt the listed tests first, observe the intended
failure, then implement. A legacy test may be deleted only after its ledger successor is implemented
and passing.

**Organization**: Tasks are grouped by user story. The technical order deliberately implements the
durable invocation center before code-first conveniences, even though US1, US2, and US3 are all P1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Safe to execute in parallel because the task targets different files and has no unmet
  dependency on another task in the same phase.
- **[Story]**: User-story traceability from `spec.md`.
- Every task names its primary file or directory.

## Phase 1: Setup and baseline

**Purpose**: Establish reproducible build evidence and the two isolated dependency envelopes.

- [X] T001 Restore and build `Elsa.Server.slnx` with .NET 10, recording the green baseline and existing warnings in the implementation session
- [X] T002 Add centrally pinned `Microsoft.CodeAnalysis.CSharp` and `BenchmarkDotNet` versions in `Directory.Packages.props`
- [X] T003 [P] Create the authoring generator project skeleton in `src/Elsa/Workflows/Design/CodeGeneration/Elsa.Workflows.Design.CodeGeneration.csproj` and its test project in `tests/Elsa/Workflows/Design/CodeGeneration/Tests/Elsa.Workflows.Design.CodeGeneration.Tests.csproj`
- [X] T004 [P] Create the isolated benchmark project in `benchmarks/Elsa/Activities/Runtime/Benchmarks/Elsa.Activities.Runtime.Benchmarks.csproj`
- [X] T005 Add the new projects to `Elsa.Server.slnx` and prove Runtime/activity projects do not gain Roslyn or BenchmarkDotNet references

---

## Phase 2: Foundational canonical contracts

**Purpose**: Establish alias-typed contracts, closed bindings, durable role records, and migration
guards shared by all stories.

**Critical**: No legacy type is deleted in this phase.

### Tests

- [X] T006 [P] Add alias/schema and absent/null/present envelope contract tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimeValueEnvelopeContractTests.cs`
- [X] T007 [P] Add closed input-binding union serialization/hash tests in `tests/Elsa/Workflows/Publishing/Api/Tests/RoleOwnedInputBindingContractTests.cs`
- [X] T008 [P] Add activity contract stable-key/fingerprint compatibility tests in `tests/Elsa/Activities/Design/Tests/ActivityContractCompatibilityTests.cs`
- [X] T009 [P] Add activity-execution role-record round-trip/version rejection tests in `tests/Elsa/Persistence/Groundwork/Tests/ActivityExecutionValueFlowDocumentTests.cs`
- [X] T010 Add architecture guards for Runtime→Design, assembly-qualified metadata, generated-carrier isolation, and future zero-memory references in `tests/Elsa/Architecture/ValueFlowArchitectureTests.cs`

### Implementation

- [X] T011 Implement portable value type/schema and persisted value-envelope primitives in `src/Elsa/Primitives/Primitives/Models/ValueTypeDescriptor.cs` and `src/Elsa/Workflows/Runtime/Core/Models/ValueEnvelope.cs`
- [X] T012 Implement stable activity contract pins, plain input definitions, atomic result definitions/projections, outcomes, and fingerprints in `src/Elsa/Activities/Runtime/Core/Models/ActivityContract.cs`
- [X] T013 Replace the generic/reference alternatives with the closed literal/request/variable/result/expression union in `src/Elsa/Workflows/Runtime/Core/Models/RuntimeInputBinding.cs`
- [X] T014 Update deterministic executable hashing and serialization for role-owned bindings and alias-only type metadata in `src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableHasher.cs` and `RuntimeInputBindingCompiler.cs`
- [X] T015 Add input snapshot, attempt, private state, completion, normalized fault, and typed trigger records in `src/Elsa/Workflows/Runtime/Core/Models/`
- [X] T016 Extend `src/Elsa/Workflows/Runtime/Core/Models/ActivityExecutionState.cs` with the versioned logical-invocation aggregate and update all construction sites without changing legacy invocation behavior yet
- [X] T017 Version and upcast Groundwork activity-execution documents in `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimeCheckpointWriter.cs` and the associated serialization mapping
- [X] T018 Add mixed-version fixtures and explicit incompatibility gates for unreconstructable started/completed legacy states in `tests/Elsa/Persistence/Groundwork/Tests/ActivityExecutionValueFlowDocumentTests.cs`

**Checkpoint**: Canonical records compile, serialize, hash, and recover while the legacy invocation
adapter still supplies existing behavior.

---

## Phase 3: User Story 1 — Plain transient activities and atomic typed results (Priority: P1) 🎯 MVP

**Goal**: Activity authors use constructor injection, plain `[Input]` properties, and one returned
typed result/outcome with no memory or argument API.

**Independent Test**: Invoke a service-bearing activity with required/defaulted inputs, typed result
projections, two outcomes, and disposable dependencies; verify one-time hydration and atomic result.

### Tests — write and fail first

- [X] T019 [P] [US1] Replace constructor/binder coverage with transient activation and plain-property hydration tests in `tests/Elsa/Activities/Runtime/Tests/ClrActivityActivatorTests.cs`
- [X] T020 [P] [US1] Add complete/suspend/fault/cancel transition algebra tests in `tests/Elsa/Activities/Runtime/Tests/ActivityTransitionContractTests.cs`
- [X] T021 [P] [US1] Replace `WriteLineBoundInputExecutionTests` with wrapper-free end-to-end hydration tests in `tests/Elsa/Activities/Runtime/Tests/WriteLineBoundInputExecutionTests.cs`
- [X] T022 [P] [US1] Add atomic typed-result/projection/outcome and partial-result rejection tests in `tests/Elsa/Activities/Runtime/Tests/ActivityCompletionContractTests.cs`
- [X] T023 [US1] Expand `tests/Elsa/Activities/Runtime/Tests/ActivityLibraryAcceptanceTests.cs` to require ordinary input properties and one result contract for every first-party activity

### Implementation

- [X] T024 [US1] Replace mutable execution output methods with `IActivity.ExecuteAsync` returning the closed transition contract in `src/Elsa/Activities/Runtime/Core/Contracts/IActivity.cs` and `Models/ActivityTransition.cs`
- [X] T025 [US1] Add author-facing generic `Activity<TResult>` and reduced cancellation/identity-only execution contexts in `src/Elsa/Activities/Runtime/Core/Models/ActivityExecutionContext.cs`
- [X] T026 [US1] Implement `IActivityActivator`, async-disposable activation leases, and fresh CLR construction in `src/Elsa/Activities/Runtime/Contracts/IActivityActivator.cs` and `src/Elsa/Activities/Primitives/Activation/ClrActivityActivator.cs`
- [X] T027 [US1] Replace `ActivityArgumentBinder` with stable-key, one-time `ActivityInputHydrator` in `src/Elsa/Activities/Primitives/Binding/ActivityInputHydrator.cs`
- [X] T028 [US1] Replace runtime memory seeding and mutable output publication with snapshot hydration and an atomic completion projector in `src/Elsa/Activities/Runtime/Services/ActivityCompletionProjector.cs`
- [X] T029 [US1] Migrate invoke and parent-completion handlers to activation leases and returned transitions in `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs` and `WorkflowParentActivityCompletionSchedulerWorkHandler.cs`
- [X] T030 [P] [US1] Migrate primitive and scheduling activities to plain inputs/results in `src/Elsa/Activities/Primitives/` and `src/Elsa/Activities/Scheduling/`
- [X] T031 [P] [US1] Migrate HTTP and scripting activities to plain inputs/atomic result records in `src/Elsa/Activities/Http/` and `src/Elsa/Activities/Scripting/`
- [X] T032 [P] [US1] Migrate sequence, composition, flowchart, and control-flow CLR activities that remain non-intrinsic in `src/Elsa/Activities/Sequence/`, `Composition/`, `Flowchart/`, and `ControlFlow/`
- [X] T033 [US1] Update CLR discovery and registration to scan plain annotated inputs and result projections in `src/Elsa/Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs`
- [X] T034 [US1] Run US1 runtime, design scanner, activity-library, HTTP, scheduling, scripting, control-flow, and sequence test projects and mark corresponding ledger rows implemented/passing

**Checkpoint**: A representative CLR activity executes end to end without `InputArgument`,
`OutputArgument`, or mutable output context behavior.

---

## Phase 4: User Story 3 — Durable pinned inputs and committed results (Priority: P1)

**Goal**: Retry, resume, restart, and downstream recovery reuse immutable invocation records.

**Independent Test**: Suspend after pinning variable/expression inputs, mutate sources, restart, resume,
and then recover after completion without reevaluation or reactivation.

### Tests — write and fail first

- [X] T035 [P] [US3] Add ActivityStarted checkpoint crash-window and all-or-nothing snapshot tests in `tests/Elsa/Workflows/Runtime/Tests/ActivityInputSnapshotCheckpointTests.cs`
- [X] T036 [P] [US3] Add retry/resume pinned-input and fresh-attempt identity tests in `tests/Elsa/Activities/Runtime/Tests/PinnedInputRetryResumeTests.cs`
- [X] T037 [P] [US3] Add post-completion crash recovery/no-reinvoke tests in `tests/Elsa/Persistence/Groundwork/Tests/CommittedActivityCompletionRecoveryTests.cs`
- [X] T038 [P] [US3] Add persistable/transient value-policy and sensitivity non-downgrade tests in `tests/Elsa/Workflows/Runtime/Tests/ValueDurabilityPolicyTests.cs`
- [X] T039 [US3] Add structural-frame/causal-lineage result selection and ambiguity tests in `tests/Elsa/Workflows/Runtime/Tests/CausalActivityResultResolverTests.cs`

### Implementation

- [X] T040 [US3] Move complete input materialization into the Scheduled→Running checkpoint in `src/Elsa/Workflows/Runtime/Services/WorkflowStartActivitySchedulerWorkHandler.cs`
- [X] T041 [US3] Make `RuntimeActivityInputMaterializer` produce wrapper-free immutable snapshots with persistability/policy validation in `src/Elsa/Workflows/Runtime/Services/RuntimeActivityInputMaterializer.cs`
- [X] T042 [US3] Make retry, resume, and parent completion hydrate only from the committed snapshot in `src/Elsa/Activities/Runtime/Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs` and related handlers
- [X] T043 [US3] Commit result/outcome/status/inspection/continuation intent atomically and short-circuit already-completed invocations in `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`
- [X] T044 [US3] Replace active/latest-output semantics with structural-frame and causal-lineage result resolution in `src/Elsa/Workflows/Runtime/Services/CausalActivityResultResolver.cs`
- [X] T045 [US3] Propagate effective persistence, external-payload, encryption, sensitivity, and redaction policy through materialization and projection in `src/Elsa/Workflows/Runtime/Services/`
- [X] T046 [US3] Run focused runtime, Groundwork, distributed recovery, and publishing tests and mark US3 ledger successors implemented/passing

**Checkpoint**: The runtime can recover a durable invocation using only its pinned snapshot, attempts,
private state, completion, and executable contract.

---

## Phase 5: User Story 2 — Typed code-first method-call authoring (Priority: P1)

**Goal**: IDE-guided workflow definitions and generated activity methods compile to the same authored
state and executable as dynamic authoring.

**Independent Test**: Compile equivalent typed code-first and dynamic workflows containing literals,
request members, variables, a conditional, a child workflow, prior results, and an expression.

### Tests — write and fail first

- [X] T047 [P] [US2] Add foundational builder ordering, `.From`, `.Value`, null/default/omitted, lexical-scope, and connection tests in `tests/Elsa/Workflows/Design/Tests/WorkflowBuilderTests.cs`
- [X] T048 [P] [US2] Add generator golden/diagnostic tests for named activity methods and call-handle shapes in `tests/Elsa/Workflows/Design/CodeGeneration/Tests/ActivityCallGeneratorTests.cs`
- [X] T049 [P] [US2] Add Build-once/version-input determinism tests in `tests/Elsa/Workflows/Design/Tests/WorkflowDefinitionCompilerTests.cs`
- [X] T050 [US2] Add paired code-first/dynamic canonical equivalence and validation tests in `tests/Elsa/Workflows/Publishing/Api/Tests/CodeFirstDynamicConformanceTests.cs`

### Implementation

- [X] T051 [US2] Implement `WorkflowDefinition<TRequest,TResult>` and the IDE-guided build/compiler entry point in `src/Elsa/Workflows/Design/Core/Authoring/WorkflowDefinition.cs`
- [X] T052 [US2] Implement sequence/structured builders, workflow request/result sources, nodes, connections, and authoring-only `ActivityArgument<T>` in `src/Elsa/Workflows/Design/Core/Authoring/`
- [X] T053 [US2] Implement lexical `Variable<T>` declaration/read handles and engine-intrinsic `Set`, return, merge, and control authored nodes in `src/Elsa/Workflows/Design/Core/Authoring/`
- [X] T054 [US2] Implement the incremental activity-call generator and stable diagnostics in `src/Elsa/Workflows/Design/CodeGeneration/`
- [X] T055 [US2] Generate call handles exposing `Node`, whole `Result`, typed `Outputs`, and typed `Outcomes` in `src/Elsa/Workflows/Design/CodeGeneration/`
- [X] T056 [US2] Lower builder state into the existing `WorkflowDefinitionState` without serializing carriers or delegates in `src/Elsa/Workflows/Design/Core/Authoring/WorkflowDefinitionCompiler.cs`
- [X] T057 [US2] Add typed child-workflow request/result calls and ordinary builder extension support in `src/Elsa/Workflows/Design/Core/Authoring/`
- [X] T058 [US2] Run generator, design, publishing golden/equivalence, and architecture tests and mark US2 ledger successors implemented/passing

**Checkpoint**: Code-first authoring is a compiler front end, not a second runtime or persisted CLR
activity object graph.

---

## Phase 6: User Story 4 — Lexical variables and deterministic structural flow (Priority: P2)

**Goal**: All mutable workflow data resides in explicit per-activation variable frames; scope returns,
parallelism, loops, and cycles are validated deterministically.

**Independent Test**: Execute nested sequential/parallel scopes and cycles, proving isolation,
explicit boundary transfer, concurrent-write rejection, and stable collection ordering.

### Tests — write and fail first

- [X] T059 [P] [US4] Replace direct memory variable tests with root/container/iteration frame conformance in `tests/Elsa/Activities/Runtime/Tests/VariableFrameRuntimeTests.cs`
- [X] T060 [P] [US4] Add concurrent-write, unavailable-producer, explicit-scope-return, and cyclic-back-edge validation tests in `tests/Elsa/Workflows/Design/Tests/ValueFlowValidatorTests.cs`
- [X] T061 [P] [US4] Add stable branch/iteration collection ordering tests across repeated randomized completion orders in `tests/Elsa/Activities/ControlFlow/Tests/DeterministicCollectionTests.cs`
- [X] T062 [US4] Replace SetVariable durability/write-back tests with intrinsic Set checkpoint/recovery tests in `tests/Elsa/Activities/Runtime/Tests/SetVariableDurabilityExecutionTests.cs`

### Implementation

- [X] T063 [US4] Implement Runtime-owned `VariableFrameState` and root/container/iteration frame creation in `src/Elsa/Workflows/Runtime/Core/Models/VariableFrameState.cs` and runtime scope services
- [X] T064 [US4] Implement explicit intrinsic Set/merge/reduce execution and checkpoint ordering in `src/Elsa/Workflows/Runtime/Services/WorkflowIntrinsicExecutor.cs`
- [X] T065 [US4] Compile variable reads, explicit scope returns, collections, merges, and reductions in `src/Elsa/Workflows/Publishing/Api/Services/ExecutableNodeCompiler.cs`
- [X] T066 [US4] Implement publication data-flow validation for concurrency, availability, scopes, cycles, and stable collection identity in `src/Elsa/Workflows/Design/Validations/`
- [X] T067 [US4] Migrate sequence, flowchart, loop, and parallel runtime scope services off memory-backed variables and metadata value bags in `src/Elsa/Activities/`
- [X] T068 [US4] Run variable, sequence, flowchart, control-flow, runtime recovery, and publishing validation tests and mark US4 ledger rows implemented/passing

---

## Phase 7: User Story 5 — Portable pure expressions (Priority: P2)

**Goal**: JavaScript, Liquid, and registered evaluators consume only immutable declared parameters and
cannot discover or mutate workflow state.

**Independent Test**: Round-trip equivalent JavaScript/Liquid definitions, restart, evaluate declared
parameters, and reject undeclared reads, mutation, delegates, and ambient nondeterminism.

### Tests — write and fail first

- [X] T069 [P] [US5] Add expression definition/hash/round-trip tests for language, source, alias result, parameters, options, and capability profile in `tests/Elsa/Expressions/Tests/ExpressionDefinitionContractTests.cs`
- [X] T070 [P] [US5] Add JavaScript immutable `args` and forbidden ambient host-function tests in `tests/Elsa/Expressions/JavaScript/Jint/Tests/ExplicitExpressionParametersTests.cs`
- [X] T071 [P] [US5] Add Liquid declared-parameter-only and service/context isolation tests in `tests/Elsa/Expressions/Tests/LiquidExplicitParametersTests.cs`
- [X] T072 [US5] Replace expression-bound activity tests with explicit-parameter materialization/restart tests in `tests/Elsa/Activities/Runtime/Tests/WriteLineExpressionInputExecutionTests.cs`

### Implementation

- [X] T073 [US5] Replace ambient `IExpressionExecutionContext` with immutable evaluation request/parameters/capability contracts in `src/Elsa/Expressions/Core/Contracts/`
- [X] T074 [US5] Extend executable expression bindings and deterministic hashing with normalized parameter bindings and options in `src/Elsa/Workflows/Runtime/Core/Models/RuntimeExpressionBinding.cs`
- [X] T075 [US5] Restrict JavaScript binding evaluation to read-only declared `args` and constructor-injected evaluator infrastructure in `src/Elsa/Expressions/JavaScript/` and `Jint/`
- [X] T076 [US5] Restrict Liquid binding evaluation to declared parameters in `src/Elsa/Expressions/Liquid/`
- [X] T077 [US5] Remove delegate expressions, captured-closure converters, ambient variable/output/workflow accessors, mutation/write-back, service location, time/random/configuration backdoors from canonical binding evaluation in `src/Elsa/Expressions/` and `src/Elsa/Workflows/Runtime/JavaScript/`
- [X] T078 [US5] Keep stateful scripting as transient activities returning typed results and requiring separate intrinsic Set operations in `src/Elsa/Activities/Scripting/`
- [X] T079 [US5] Run expressions, JavaScript, Liquid, activity materialization, hashing, and architecture tests and mark US5 ledger rows implemented/passing

---

## Phase 8: User Story 6 — Typed private state and triggers (Priority: P2)

**Goal**: Stateful activities suspend and resume fresh activations with one immutable state document
and one validated typed trigger delivery.

**Independent Test**: Suspend, dispose, restart elsewhere, deliver a typed trigger twice, and prove one
fresh resume attempt and one committed completion.

### Tests — write and fail first

- [X] T080 [P] [US6] Add typed stateful activity initial/resume contract tests in `tests/Elsa/Activities/Runtime/Tests/StatefulActivityContractTests.cs`
- [X] T081 [P] [US6] Add state-plus-registration atomic checkpoint and wrong/duplicate trigger tests in `tests/Elsa/Activities/Runtime/Tests/TypedTriggerDeliveryTests.cs` and `TypedTriggerResumeDeliveryTests.cs`
- [x] T082 [US6] Add Groundwork mixed-worker suspend/resume/disposal/recovery tests in `tests/Elsa/Persistence/Groundwork/Tests/TypedActivityStateRecoveryTests.cs`

### Implementation

- [X] T083 [US6] Implement typed `StatefulActivity<TResult,TState,TTrigger>` and resume context contracts in `src/Elsa/Activities/Runtime/Core/Models/`
- [X] T084 [US6] Persist immutable private state and typed trigger registrations atomically in `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`
- [x] T085 [US6] Validate/deduplicate typed trigger delivery before fresh activation in `src/Elsa/Activities/Runtime/Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs`
- [x] T086 [US6] Preserve existing start-authority/provider-recognition ordering while adding typed resume metadata in `src/Elsa/Workflows/Runtime/Resumption/`
- [X] T087 [US6] Migrate scheduling, HTTP, event, timer, and bookmark-producing activities to typed state/trigger transitions in `src/Elsa/Activities/`
- [X] T088 [US6] Run resumption, scheduling, HTTP, runtime, Groundwork, and disposal tests and mark US6 ledger rows implemented/passing

---

## Phase 9: User Story 7 — One-way Elsa 3 import (Priority: P3)

**Goal**: Import representable memory-reference graphs into canonical value roles and reject all
others precisely without adding canonical memory compatibility.

**Independent Test**: Import the representative valid/invalid matrix, including output-only and
combined values, and assert successful authored state contains no legacy/runtime carrier.

### Tests — write and fail first

- [x] T089 [P] [US7] Add output-only and expression-plus-memory-reference fixtures in `tests/Elsa3/Mapping/Tests/Elsa3ActivityToStateTests.cs`
- [X] T090 [P] [US7] Add literal/request/variable/result/loop/JavaScript/Liquid import matrix in `tests/Elsa3/Mapping/Tests/Elsa3MemoryReferenceImportTests.cs`
- [X] T091 [P] [US7] Add dangling/multiple-producer/cross-subtree/ambiguous/cyclic/custom/dynamic-script diagnostic tests in `tests/Elsa3/Mapping/Tests/Elsa3MemoryReferenceDiagnosticsTests.cs`
- [X] T092 [US7] Expand importer-boundary architecture tests in `tests/Elsa/Architecture/Elsa3MigrationBoundaryTests.cs`

### Implementation

- [x] T093 [US7] Collect importer-local reference occurrences with paths, directions, stable keys, nodes, and structural frames in `src/Elsa3/Mapping/Services/Elsa3MemoryReferenceGraph.cs`
- [X] T094 [US7] Lower unique producer/consumer and variable relationships to canonical authored bindings in `src/Elsa3/Mapping/Mappings/Elsa3ActivityToState.cs`
- [X] T095 [US7] Rewrite only statically provable JavaScript/Liquid ambient reads into declared parameters and reject dynamic access in `src/Elsa3/Mapping/Services/Elsa3ExpressionRewriter.cs`
- [X] T096 [US7] Emit stable path-specific importer diagnostics and preserve output-only/combined reference meaning in `src/Elsa3/Mapping/Services/Elsa3WorkflowDefinitionImporter.cs`
- [X] T097 [US7] Run the full importer matrix and architecture boundary tests and mark US7 ledger rows implemented/passing

---

## Phase 10: Clean legacy removal and canonical convergence

**Purpose**: Delete the adapter only after every affected behavioral objective has a passing successor.

- [X] T098 Verify every applicable Retain/Replace/Remove row in `specs/095-value-flow-redesign/test-migration-ledger.md` is implemented/passing before deleting a legacy test or source
- [X] T099 Remove `IMemoryBlock`, `IMemoryBlockReference`, `IMemoryRegister`, their factory/implementations, and memory-backed variable inheritance from `src/Elsa/Expressions/`
- [X] T100 Remove `Argument`, `InputArgument<T>`, `OutputArgument<T>`, legacy activity factory/constructor binding, runtime input seeding, mutable output recording/publication, and forwarding registrations from `src/Elsa/Activities/` and `src/Elsa/Workflows/Runtime/`
- [X] T101 Remove `IActivity.SyntheticProperties` as a workflow-value channel, active/latest-output semantic truth, generic reference bindings, and assembly-qualified input metadata from canonical `src/Elsa/`
- [X] T102 Delete or rewrite obsolete memory-fixture tests only after recording their passing successor in `specs/095-value-flow-redesign/test-migration-ledger.md`
- [X] T103 Make `tests/Elsa/Architecture/ValueFlowArchitectureTests.cs` enforce zero canonical source/public/internal references and the narrow Elsa 3 importer allowlist
- [X] T104 Build `Elsa.Server.slnx` and run all focused migrated projects after clean removal

**Checkpoint**: The repository has one canonical Elsa 4 value-flow path and no forwarding shim.

---

## Phase 11: User Story 8 — DI isolation evidence (Priority: P3)

**Goal**: Select activity DI lifetime using reproducible measurements after the real activation and
intrinsic paths exist.

**Independent Test**: Run all required workloads under all eligible candidates and apply correctness,
isolation, transitive-dependency, retry/resume, and disposal gates before comparing speed.

### Tests and benchmark implementation

- [X] T105 [P] [US8] Implement deterministic semantic assertions/counters for activation, scope, identity, and disposal in `benchmarks/Elsa/Activities/Runtime/Benchmarks/ActivationScopeSemanticTests.cs`
- [X] T106 [P] [US8] Implement no-op, transient, scoped-disposable/transitive, intrinsic-heavy, mixed, I/O, retry, resume, and concurrent-drain workloads in `benchmarks/Elsa/Activities/Runtime/Benchmarks/ActivationScopeBenchmarks.cs`
- [X] T107 [US8] Implement burst-only, per-attempt, and provably eligible conditional activation strategies behind `IActivityActivator` in `benchmarks/Elsa/Activities/Runtime/Benchmarks/ActivationStrategies.cs`
- [X] T108 [US8] Run BenchmarkDotNet with retained environment/config/raw results under `benchmarks/Elsa/Activities/Runtime/Benchmarks/results/`
- [X] T109 [US8] Reject any semantically invalid candidate, compare throughput/p50/p95/allocations, and record the selected observable lifetime in `docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md` or a focused successor
- [X] T110 [US8] Apply the selected lifetime behind `IActivityActivator` and run runtime isolation/disposal/retry/resume tests

---

## Phase 12: Final verification and documentation

**Purpose**: Reconcile every requirement and ensure generated maps and canonical documents reflect the
implemented repository.

- [X] T111 [P] Supersede/reconcile specs 006, 011, 015, 029, 060, 061, 083, and 090 plus ADR 0030 with links to spec 095 in their current-status sections
- [X] T112 [P] Add major-version migration and removal notes for activity/expression authors under `docs/` without duplicating canonical glossary definitions
- [X] T113 Refresh the narrowest relevant generated architecture, domain, extension-point, and feature-dependency maps using `tools/maps/generate-*.sh` and review generated findings
- [X] T114 Run all focused commands in `specs/095-value-flow-redesign/quickstart.md`, then run `/usr/local/share/dotnet/dotnet test Elsa.Server.slnx`
- [X] T115 Search canonical source, tests, packages, serialized goldens, and service registrations for every forbidden legacy/generated/assembly-qualified carrier and resolve all non-importer hits
- [X] T116 Audit FR-001–FR-065 and SC-001–SC-014 against tests, benchmark evidence, code, and the migration ledger; record evidence and close every task
- [X] T117 Run `git diff --check`, review the full diff for accidental user-change overlap, and create the required local implementation commit

---

## Dependencies and execution order

### Phase dependencies

- Phase 1 precedes all new-project work.
- Phase 2 establishes canonical types used by every story.
- US1 establishes activation/hydration/result semantics.
- US3 makes those semantics durable and is required before US2 can prove canonical equivalence.
- US2 can then author every canonical value role.
- US4, US5, and US6 depend on the durable invocation center and may proceed in parallel by project,
  but their integration tests converge in Runtime.
- US7 depends on the final authored binding/expression model.
- Clean removal depends on every relevant ledger successor from US1–US7 passing.
- US8 depends on the real activator and intrinsic path and precedes the final lifetime implementation.
- Final verification depends on all stories and clean removal.

### Test-first rule

Within each story phase:

1. Add/adapt the contract and integration tests.
2. Run the focused test and observe failure for the missing target behavior.
3. Implement the smallest coherent production slice.
4. Run the focused suite and update the ledger status.
5. Do not delete a legacy test until its successor is passing.

### Parallel opportunities

- New generator and benchmark project skeletons are independent.
- In US1, first-party library migrations may run in parallel after the core activator/result API lands.
- US4 frame work, US5 evaluator work, and US6 typed trigger work are project-separated after their
  shared Runtime Core records stabilize.
- Importer fixtures and architecture guards can be prepared independently of importer lowering.
- Documentation/migration notes and final map-refresh preparation are independent until final audit.

## Implementation strategy

Deliver one vertical durable activity slice first: alias-typed contract → normalized bindings →
ActivityStarted input snapshot → transient activation/plain hydration → returned typed result/outcome
→ atomic completion/recovery. Keep the legacy adapter only while it supplies still-unmigrated tests;
do not expose it through new contracts. Once the vertical slice proves the center, migrate authoring,
variables, expressions, stateful activities, first-party libraries, and import toward it. Remove the
adapter in one clean convergence phase, benchmark the real activation seam, and finish with a
requirement-by-requirement audit.

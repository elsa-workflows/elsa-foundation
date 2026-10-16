---

description: "Task list for spec 238: Flowchart starts at the trigger that fired"
---

# Tasks: Flowchart Starts at the Trigger That Fired

**Input**: Design documents from `specs/238-flowchart-trigger-start/` ([spec](spec.md), [plan](plan.md),
[research](research.md), [data-model](data-model.md), [quickstart](quickstart.md)).

**Tests**: Required by framework §2.23.2. The engine start selection, loop-edge roots, multi-entry dominance and
exclusive-write rules are logic-bearing risky paths, so each gets behaviour tests. No feature-registration test is
needed: no feature wiring changes.

**Cadence**: This changes existing code, so it is not greenfield (§2.21.2), and the Elsa constitution declares no
TDD. Each story implements first and then adds its tests in the same phase. A story is not done until its tests
pass.

**Paths**: All paths are relative to the repository root of worktree `w238`. `src/` and `tests/` mean
`Essentials/src/` and `Essentials/tests/`.

**Build rule**: Build and test only the projects named in a task (shared machine). Allow for the build-slot wrapper's
wait.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1, US2 or US3 from spec.md

---

## Phase 1: Setup (baseline)

**Purpose**: Record today's results so FR-012 and SC-004 ("unchanged") can be shown, not asserted.

- [X] T001 [P] Run `dotnet test Essentials/tests/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj` on the unchanged tree and record the pass/fail/skip counts in `specs/238-flowchart-trigger-start/tasks.md` under "Baseline" below.
- [X] T002 [P] Run `dotnet test Essentials/tests/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj --filter "FullyQualifiedName~ValueFlowValidatorTests"` on the unchanged tree and record the counts under "Baseline".
- [X] T003 [P] Run `dotnet test Essentials/tests/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj` on the unchanged tree and record the counts under "Baseline".

### Baseline

Unchanged tree, origin/main `f4ddd62f2`, 2026-10-16:

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| `Elsa.Activities.Flowchart.Tests` | 105 | 0 | 0 |
| `Elsa.Workflows.Design.Tests` (`ValueFlowValidatorTests`) | 9 | 0 | 0 |
| `Elsa.Activities.Http.IntegrationTests` | 71 | 2 | 0 |

The two HTTP integration failures exist before any change:
- `HttpEndpointRouteTableConvergenceEndToEndTests.An_http_bookmark_created_on_one_node_is_resumed_through_the_other_within_the_bound`:
  a Windows `IOException` on `runtime.db` during `DisposeAsync`.
- `DrainCyclesWithoutProgressTests.A_run_whose_Parallel_suspends_one_branch_runs_every_WriteLine_in_the_other(mode: Balanced)`.

---

## Phase 2: Foundational

**Purpose**: The one graph method that both the runtime stories and their regression tests depend on.

- [X] T004 Add `public ExecutableNode? SelectEntryNode(string? triggerNodeId)` to `src/Activities/Flowchart/Internal/FlowchartGraph.cs`, directly below `SelectStartNode()`. When `triggerNodeId` is non-null and `_nodesById` contains it, return that node; otherwise return `SelectStartNode()`. Add an XML doc stating three rules: only a **direct** child counts, because the trigger id is workflow-wide; the method is per run, while `SelectStartNode` stays the static fallback; and `ComputeBackwardEdges` must keep using `SelectStartNode` (research R1, R2).

**Checkpoint**: The Flowchart project builds: `dotnet build Essentials/src/Activities/Flowchart/Elsa.Activities.Flowchart.csproj`.

---

## Phase 3: User Story 1 - A multi-trigger flowchart runs the branch whose trigger fired (Priority: P1) 🎯 MVP

**Goal**: A Flowchart whose direct child is the run's trigger starts at that child (FR-001 to FR-005).

**Independent Test**: Publish a Flowchart with two start-capable HTTP endpoints, fire the second one, and see only
its branch run, with no bookmark left (quickstart §1–§2).

### Implementation

- [X] T005 [US1] In `src/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`, replace `graph.SelectStartNode()` with `graph.SelectEntryNode(context.TriggerNodeId)` in `StartAsync` (the `startNode` local), in `OnChildCompletedAsync`'s `CreateInitialState(...)` fallback, and in `OnChildFaultedAsync`'s `CreateInitialState(...)` fallback (FR-001, FR-003). Leave every other line unchanged.
- [X] T006 [US1] In `src/Activities/Flowchart/Internal/FlowchartGraph.cs` `ComputeBackwardEdges`, change the roots to: the start node (`SelectStartNode()`), then the ids of `Children` whose `Metadata` has `TriggerNodeMetadata.ExecutionTypeKey` equal to `TriggerNodeMetadata.TriggerExecutionType` (ordinal-sorted), then all `_nodesById` keys (ordinal-sorted), with `.Distinct(StringComparer.Ordinal)`. Update the method's doc comment to say why trigger children come second (FR-005, research R2). Add `using Elsa.Workflows.Runtime.Core.Constants;` if needed.

### Tests

- [X] T007 [US1] In `tests/Activities/Flowchart/Tests/FlowchartRuntimeFixture.cs`, add an `ExecuteAsync(WorkflowExecutable executable, string triggerNodeId)` overload. It calls `WorkflowExecutionHarness.RunAsync(executable, stimulusInput, triggerNodeId)` (`tests/Activities/Testing/WorkflowExecutionHarness.cs:116`), passing an empty JSON object as the stimulus, and returns the same run result as the existing `ExecuteAsync`.
- [X] T008 [US1] Create `tests/Activities/Flowchart/Tests/FlowchartTriggerStartTests.cs` with the xUnit tests below. Build the graphs like `FlowchartExecutionEngineTests`, with an explicit `startNodeId`, and assert with `AssertRan` / `AssertDidNotRun`:
  - `TriggerChild_IsFirstNode_StartNodeDoesNotRun`: start `a → a2` and second branch `b → b2`; trigger `b`. Then `b` and `b2` ran, and `a` and `a2` did not.
  - `TriggerBranches_JoinSharedNode_SharedNodeRunsOnce`: `a → shared` and `b → shared`; trigger `b`. Then `shared` ran once and `a` did not.
  - `TriggerChild_WithInboundConnection_StartsThereAsRootPath`: `a → b → c`; trigger `b`. Then `b` and `c` ran and `a` did not.
  - `TriggerIsStartNode_BehavesAsToday`: trigger `a`, where `a` is the start node. Then the same nodes ran as on a direct run.
- [X] T009 [US1] Add `TriggerOnlyLoop_ClassifiesLoopClosingEdgeFromTrigger` to `tests/Activities/Flowchart/Tests/FlowchartTriggerStartTests.cs` (kept beside the other spec 238 tests rather than in `FlowchartCyclicJoinTests.cs`):
  - Graph: start `s` with no outbound connections; trigger-marked `t` (hand-built `ExecutableNode` with `metadata: { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType }`); `t → node-y → node-a → node-y`. The ids are chosen so ordinal order would root the walk at `node-a`.
  - Assert through `FlowchartReachabilityAnalyzer.IsBackwardEdge` that `node-a → node-y` is backward and `node-y → node-a` is not (FR-005, research R2).
  - Add `TriggerReachableFromStart_EdgeClassificationUnchanged`, where `t` is reachable from `s`, asserting the same backward edges as the equivalent unmarked graph.
- [X] T010 [US1] Create `tests/Activities/Behavioral/Tests/FlowchartTriggerStartBehaviorTests.cs` with real activities, built with `Nodes.Leaf` / `Nodes.Structural` as in `Drives/HttpDrives.cs` (`HttpEndpointDrive`) and `Drives/FlowchartDrive.cs`:
  - `TwoHttpEndpoints_SecondFires_RunsSecondBranchOnly_NoBookmarkLeft`: start node HTTP endpoint A (`CanStartWorkflow=true`) → WriteLine/probe A2; second HTTP endpoint B (`CanStartWorkflow=true`) → probe B2. Run with `harness.RunAsync(..., request, triggerNodeId: "B")`. Assert the workflow completed, B and B2 ran, A and A2 did not, and the bookmark store holds no bookmark for the run (read through `harness.Services`, as `HttpEndpointExecutionTests.BookmarksAsync` does).
  - `CronStartNode_HttpEndpointFires_RunsEndpointBranchOnly`: start node Cron → C2; HTTP endpoint H → H2; trigger `H`. Assert H2 ran and C and C2 did not.
  - `CronStartNode_CronFires_RunsCronBranchOnly`: same graph; run with `RunAsTriggerDeliveryAsync(executable, "C", metadata)`. Assert C2 ran and H and H2 did not, with no bookmark left.
- [X] T011 [US1] Add `FlowchartTriggerStart_ForLoopFlowchartBody_RestartsAtTriggerEachPass` to `tests/Activities/Runtime/Tests/BreakPropagationExecutionTests.cs`. It reuses that class's private For and Flowchart node builders instead of duplicating them in a new file:
  - A `For` loop with 3 iterations whose body is a Flowchart. The body has start probe `a` and a trigger branch `t → probe-b`, built with a local `NewFlowchartNode` helper like `BreakPropagationExecutionTests.cs:197`.
  - Run with `triggerNodeId: "t"`. Assert `probe-b` ran 3 times and `a` never ran (D3, FR-004).
  - If `t` is a probe leaf, give it a probe node; trigger delivery is only needed for stateful triggers.
- [X] T012 [US1] Make the HTTP integration host able to publish a Flowchart:
  - Add a `ProjectReference` to `src/Activities/Flowchart/Elsa.Activities.Flowchart.csproj` in `tests/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj`.
  - Add `ActivitiesFlowchartFeature` to the feature list in `tests/Activities/Http/IntegrationTests/HttpEndpointHostFixture.cs` (around l.217-228).
  - Add `PublishTwoHttpEndpointFlowchartWorkflowAsync(string firstPath, string secondPath)`, modelled on `PublishSequencedHttpEndpointsWorkflowAsync` (l.465). It uses `NewHttpEndpointNode(..., canStartWorkflow: true, isTriggerNode: true, nodeId)` for both endpoints, each heading its own branch, and the first endpoint as `startNodeId`.
- [X] T013 [US1] Create `tests/Activities/Http/IntegrationTests/MultiTriggerFlowchartEndToEndTests.cs` with `SecondEndpointRequest_StartsAtSecondEndpoint_CompletesWithoutBookmarks` (FR-016, SC-001, SC-002):
  - Publish through T012's helper and assert `ListTriggerBindingsAsync` holds two bindings.
  - Send a request to the second path through `Client`.
  - Assert through `SingleWorkflowExecutionAsync()` that the run completed and its trigger node was the second endpoint, and that `ListBookmarksAsync(workflowExecutionId)` is empty.
  - Add `FirstEndpointRequest_StartsAtFirstEndpoint` as the mirror case.
- [X] T014 [US1] Run `dotnet test` for `Elsa.Activities.Flowchart.Tests`, `Elsa.Activities.Behavioral.Tests --filter "FullyQualifiedName~FlowchartTriggerStart"`, `Elsa.Activities.Runtime.Tests --filter "FullyQualifiedName~FlowchartTriggerStart"` and `Elsa.Activities.Http.IntegrationTests`. Every new test must pass, and the pre-existing counts must match the T001 and T003 baseline.

**Checkpoint**: US1 is shippable on its own. Multi-trigger flowcharts run the fired branch.

---

## Phase 4: User Story 2 - Value flow is validated per entry point (Priority: P1)

**Goal**: The value-flow check treats `startNodeId` plus every direct child with no inbound connection as an entry
point (FR-008 to FR-013).

**Independent Test**: The validator scenarios in quickstart §3 pass without a catalog.

### Implementation

- [X] T015 [US2] In `src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/Validators/ValueFlowValidator.cs`, change `FlowchartControlFlow.TryCreate(JsonElement? payload)` to `TryCreate(JsonElement? payload, IReadOnlyCollection<string> childNodeIds)`:
  - Compute `entryNodeIds` = `startNodeId` plus each `childNodeIds` element that no edge targets, distinct and ordinal-ordered.
  - Include every entry in `nodeIds`.
  - Pass `entryNodeIds` to the constructor in place of the single `startNodeId`.
  - Keep the early `return null` when `startNodeId` is blank (edge case: no authored start node).
- [X] T016 [US2] In the same file, update `ValueFlowGraph.Flowchart(NodeInfo container)` to pass the direct children's ids: `_nodes.Where(n => ReferenceEquals(n.Parent, container) && !string.IsNullOrWhiteSpace(n.Node.NodeId)).Select(n => n.Node.NodeId)`.
- [X] T017 [US2] In the same file, generalise `FlowchartControlFlow.Dominates` to multiple entries (FR-009, FR-011, research R4):
  - `reachable` is the union over entries of `IsReachable(entry, id)`.
  - Each entry's dominator set is `{entry}`; every other reachable node starts at the full `reachable` set.
  - The fixed-point loop iterates reachable nodes that are not entries.
  - With one entry the code must compute exactly what it does today.
- [X] T018 [US2] In the same file, add `FlowchartControlFlow.EntriesReaching(string node)`, which returns the entries from which `IsReachable(entry, node)` holds. Then extend `ArePotentiallyConcurrent`'s Flowchart branch (FR-010, research R5): the writes are concurrent only when neither reaches the other **and** they are not exclusive. They are exclusive when both entry sets are non-empty and do not overlap.

### Tests

- [X] T019 [US2] Add these tests to `tests/Workflows/Design/Tests/ValueFlowValidatorTests.cs`, using the existing `Flowchart(...)` JSON helper and `ActivityResult` references. Assert error `Type` values only, never message text (§2.23):
  - `TriggerBranchConsumer_ReadsOwnTriggerResult_IsAccepted`: start `a`; `b` with no inbound edge, `b → c`; `c` reads `b`. No `ValueFlow/UnavailableProducer` (US2-1).
  - `TriggerBranchConsumer_AlsoReachedFromStart_IsRejected`: as above plus `a → c`. `ValueFlow/UnavailableProducer` (US2-2).
  - `WritesOnDifferentTriggerBranches_AreNotConcurrent`: `a → setA` and `b → setB`, both writing variable `v`. No `ValueFlow/ConcurrentWrite` (US2-3).
  - `ForkFromOneEntry_WritesStillConcurrent`: `a → x`, `a → y`, both writing `v`. `ValueFlow/ConcurrentWrite` (US2-4).
  - `MidFlowNodeWithInbound_IsNotEntry_ProducerStillDominates`: `a → p → h → c`; `c` reads `p`. No error (US2-5).
  - `TriggerBranchLoop_CyclicBackEdgeStillDetected`: `b → x → y → x` with no inbound edge to `b`; `x` reads `y`. `ValueFlow/CyclicBackEdge` (FR-011).
- [X] T020 [US2] Add single-entry regression cases to the same file (FR-012), one each for dominance (accepted and rejected), concurrent writes (fork) and cyclic back-edge, on graphs where every non-start child has an inbound edge. Skip any case an existing test already covers and name that test in a comment.
- [X] T021 [US2] Run `dotnet test Essentials/tests/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj --filter "FullyQualifiedName~ValueFlowValidatorTests"`. Every pre-existing test must keep the result recorded in T002. If one changes because a disconnected child became an entry point, stop and record the test name and reason for the PR (research R6, §2.21.1) rather than editing the test.

**Checkpoint**: Drafts with several triggers validate and publish.

---

## Phase 5: User Story 3 - Single-start flowcharts behave exactly as before (Priority: P1)

**Goal**: Show that no single-start or direct-run behaviour changed (FR-002, FR-006, SC-004).

**Independent Test**: The regression tests below pass, and the existing suites match the Phase 1 baseline.

- [X] T022 [P] [US3] Add these tests to `tests/Activities/Flowchart/Tests/FlowchartTriggerStartTests.cs`:
  - `DirectRun_NoTrigger_StartsAtStartNode`.
  - `TriggerIdNotADirectChild_StartsAtStartNode`: trigger id `"elsewhere"`.
  - `NestedFlowchart_TriggerIsInnerChild_OuterStartsAtStartNode_InnerStartsAtTrigger`: the outer start `a → inner`, where the inner Flowchart has start `i1` and trigger child `i2 → i3`; trigger `i2`. Then `a`, `inner`, `i2` and `i3` ran and `i1` did not (D2).
- [X] T023 [US3] Confirm that no file under `src/Activities/Sequence/`, `src/Activities/Bpmn/` or `src/Activities/ControlFlow/` changed: `git diff --stat origin/main -- Essentials/src/Activities/Sequence Essentials/src/Activities/Bpmn Essentials/src/Activities/ControlFlow` prints nothing (FR-006).
- [X] T024 [US3] Re-run the T001–T003 suites and confirm that the pre-existing counts equal the baseline (SC-004).

---

## Phase 6: Polish & Cross-Cutting

- [X] T025 [P] Add a short "Where a run starts" section to `src/Activities/Flowchart/README.md` covering four points (no restatement of glossary terms):
  - A run starts at its trigger node when that node is a direct child.
  - It falls back to the start node otherwise.
  - A nested Flowchart honours only its own children.
  - A loop restarts the Flowchart at the trigger on every pass.
- [X] T026 [P] (Checked: the README lists only error codes, so it is unchanged.) Check whether `src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/README.md` describes the value-flow check's start node. If it does, add one sentence on entry points (no-inbound children). If it does not, change nothing.
- [X] T027 Run `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~ArchitectureGuardTests"` (§E2.2).
- [X] T028 Run `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` and `git diff --stat docs/`. Only a map that changed for this unit's reason is staged, by explicit path, with `docs/maps/manifest.json`. On Windows, `-- check` reports every map stale because of CRLF, so don't rely on it.
- [X] T029 Set `**Status**` in `specs/238-flowchart-trigger-start/spec.md` to "Implemented — PR for #2850", and tick T001–T029 here.
- [X] T030 Commit on `238-flowchart-trigger-start` and, with Joey's go-ahead, push and open draft PR #2856 against `main`. The gate evidence comment is held until the placement revision lands (it is posted in T058).

---

## Phase 7: Placement revision setup (D8–D13)

**Purpose**: Settle the open points the revision depends on before writing code.

- [X] T031 Verify the startup-reconciliation risk (research R12):
  - Trace `WorkflowArtifactReconciler` for a mounted artifact that is already active and identical. Does it reach
    `TryFindTriggerSurfaceFault` → `Evaluate`?
  - Record the answer under "Revision notes" below.
  - If it does and there is no clean way to skip the re-evaluation for an identical, already-active artifact, stop
    and ask Joey before T040.
- [X] T032 [P] Verify FR-027: does `BpmnDocumentImporter` (`src/Activities/Bpmn/Interchange/Services/BpmnDocumentImporter.cs:225-233`) emit nested `BpmnProcess` nodes, and can they carry event-defined starts? Record the evidence under "Revision notes". If it emits none, mark T046 void.
- [X] T033 [P] Baseline the revision suites on the current branch tip and record the counts under "Revision notes":
  - `Elsa.Workflows.Runtime.Tests`
  - `Elsa.Workflows.Runtime.Scheduling.Tests`
  - `Elsa.Activities.Sequence.Tests`
  - `Elsa.Workflows.Publishing.Api.Tests`
  - `Elsa.Activities.Bpmn.Interchange.Tests`
  - `Elsa.Activities.Bpmn.Tests`
  - the Studio client tests for `flowchartStartNode`

### Revision notes

- **T031 (startup reconciliation)**:
  - Every pass runs Gate 3b (`TryFindTriggerSurfaceFault` → `Evaluate`) for each mounted closure whose envelope
    carries bindings, before the already-active check.
  - `RejectUnit` only logs and records a report entry, and `WorkflowArtifactReconcilerStartupTask` logs rejections
    instead of throwing.
  - So an already-active mounted artifact with a misplaced start trigger **stays active**, with its bindings
    untouched (FR-025). The only effect is a startup log line naming the trigger and the guidance.
  - No reconciliation change is needed, so T040 is reduced to the import test in T050.
- **T032 (BPMN nesting)**:
  - `BpmnDocumentImporter.MaterializeChild` maps `BpmnWorkBinding.NestedProcess` to `BuildProcessNode`, a nested
    `Elsa.BpmnProcess` node with no inputs, so `CanStartWorkflow` defaults to true.
  - On `BpmnProcess`, `CanStartWorkflow` gates only start-binding emission (the trigger stimulus and recurring
    schedule providers). So authoring it false on nested processes is safe, and T046 stands.
- **T033 baselines** (branch tip `ec8e55289`):

| Suite | Passed | Failed |
|---|---|---|
| `Elsa.Workflows.Runtime.Tests` | 2313 | 0 |
| `Elsa.Workflows.Runtime.Scheduling.Tests` | 87 | 0 |
| `Elsa.Workflows.Runtime.Reconciliation.Tests` | 30 | 0 |
| `Elsa.Activities.Sequence.Tests` | 18 | 0 |
| `Elsa.Workflows.Publishing.Api.Tests` | 750 | 0 |
| `Elsa.Activities.Bpmn.Interchange.Tests` | 106 | 0 |
| `Elsa.Activities.Bpmn.Tests` | 251 | 0 |

  Studio client tests cannot run on this machine (no Node.js or pnpm installed), so Studio CI runs them.
- **Implementation notes (T034–T057)**:
  - **T036.** Start-entry provider failures (`ArgumentException`, `FormatException`, `InvalidOperationException`)
    are wrapped as facet `StartEntryRecognition`, and a duplicate kind as facet `StartEntryProviderIdentity`. Both
    are non-placement facets. The Flowchart and Sequence providers rethrow their own structure exceptions as
    `InvalidOperationException`, so a malformed structure cannot escape preflight untyped.
  - **T037.** No DI change was needed: `TryAddSingleton<IWorkflowTriggerBindingExtractor, WorkflowTriggerBindingExtractor>()`
    picks the overload that takes `IEnumerable<IStructuralStartEntryProvider>`.
  - **T040.** No reconciliation change, per T031.
  - **T043.** Tests changed, each for the same reason (FR-028). Their roots were structure-less `Elsa.Sequence`
    nodes with start triggers below them, which the start-path rule refuses. Each now carries a test structure plus
    a test start-entry provider, and what each test asserts is unchanged:
    - `WorkflowTriggerBindingExtractorTests.Evaluate_RejectsWholePreflightWhenLaterTriggerNodeIsInvalid`
    - `WorkflowTriggerBindingExtractorTests.Evaluate_CallsEveryProviderExactlyOncePerTriggerNode`
    - `WorkflowTriggerBindingExtractorTests.Extract_WalksChildSlots`
    - `WorkflowTriggerIndexerTests.Index_InvalidLaterNode_LeavesEverySeededBindingUntouched`
    - The two `RecurringTriggerScheduleIndexerTests` the census listed were not affected (87/87 unchanged).
  - **T047.** `PublishMisplacedStartTriggerEndpointTests` drives the real publish endpoint and handler with an
    extractor that fails with each facet. A `Placement` failure answers 400 with no executable and no activation
    slot saved; another facet does not answer 400. The real placement walk is covered by T042 and T048.
  - **T049.** Covered by `ImportGateStartTriggerPlacementTests.A_misplaced_start_trigger_without_carried_bindings_is_never_activated`.
    It drives the activation coordinator → `IWorkflowTriggerIndexer.PrepareActivationAsync` → extractor path,
    which `RestorePublicationSlotRequestHandler` also uses. No separate restore-endpoint test was added.
  - **T054.** Studio's reachability warning ("cannot be reached from the start node") also contradicted D8: a
    second trigger's branch was flagged unreachable. Its walk now starts from the start node plus every start
    trigger that nothing connects into.
  - **T055.** Studio tests changed:
    - `flowchartStartNode.test.ts` `findMisplacedStartTriggers`: the repro case and the "connected into" case
      swapped expectations under D13, and multi-trigger and mid-flow cases were added.
    - `draftValidationSurface.test.tsx` "start-trigger detection": the same flip, plus a multi-trigger case that
      asserts neither warning.
- **T051 results** (whole projects, at `bfde21cec`):

| Suite | Result | Versus baseline |
|---|---|---|
| `Elsa.Workflows.Runtime.Tests` | 2324/2324 | +11 placement tests |
| `Elsa.Workflows.Runtime.Scheduling.Tests` | 87/87 | unchanged |
| `Elsa.Workflows.Runtime.Reconciliation.Tests` | 32/32 | +2 import placement tests |
| `Elsa.Activities.Flowchart.Tests` | 119/119 | +5 provider tests |
| `Elsa.Activities.Sequence.Tests` | 22/22 | +4 provider tests |
| `Elsa.Workflows.Publishing.Api.Tests` | 752/752 | +2 endpoint tests |
| `Elsa.Workflows.Publishing.Tests` | 48/48 | — |
| `Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests` | 157/157 | — |
| `Elsa.Workflows.Runtime.Http.Tests` | 92/92 | — |
| `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests` | 943 passed, 3 skipped | — |
| `Elsa.Activities.Bpmn.Tests` | 251/251 | unchanged |
| `Elsa.Activities.Bpmn.Interchange.Tests` | 107/107 | +1 importer test |
| `Elsa.Activities.Scheduling.Tests` | 23/23 | — |
| `Elsa.Activities.Http.IntegrationTests` | 79/80 | +5 placement tests |
| `Elsa.Activities.Behavioral.Tests` | 185/185 | — |
| `Elsa.Activities.Runtime.Tests` | 456/456 | — |
| `Elsa.Architecture.Tests` | 542/548 | — |

  The failures are the same Windows file locks seen before this unit:
  - HTTP integration: one `HttpEndpointRouteTableConvergenceEndToEndTests` case failed on a `runtime.db` lock in
    `DisposeAsync`, as in the T003 baseline.
  - Architecture: 6 `PackageVersioningPackTests` failed on temp-directory `UnauthorizedAccessException`.
- **T052**: the Workbench was built and run from this branch on `http://localhost:5395`.
  - `e2e-tests/http/Test-HttpEcho.ps1` (4/4), `events/Test-Event.ps1` and `scheduling/Test-Cron.ps1` all passed,
    each with a start trigger at Sequence child 0.
  - A scratch script publishing a start-capable Event at Sequence child 1 got 400, with the placement guidance in
    `detail`.
- **T058 bite-proof**: the extractor's placement call and the translator's 400 mapping were disabled, then restored
  from HEAD.

| Tests | Fix disabled | Fix restored |
|---|---|---|
| `WorkflowTriggerPlacementTests` | 7/11 failed | 11/11 |
| `PublishMisplacedStartTriggerEndpointTests` | 1/2 failed | 2/2 |
| `MisplacedStartTriggerEndToEndTests` | 3/5 failed | 5/5 |
| `ImportGateStartTriggerPlacementTests` | 2/2 failed | 2/2 |

  The cases that pass with the fix disabled are the accepted placements.
- **Code Review round 1** (2026-10-16, at `39bc922`):
  - **Blocking build break:** `Elsa.Activities.Http.CommandCost.Tests` links `HttpEndpointHostFixture.cs`
    without a Flowchart reference. The Flowchart builders and the Flowchart feature registration moved into
    `HttpEndpointHostFixture.Flowchart.cs`, which only the integration tests compile, behind a
    `static partial void ConfigureOptionalCompositeFeatures` hook. The full `Elsa.Server.slnx` build is now run
    before pushing.
  - **Import:** `WorkflowArtifactReconciler.TryFindTriggerSurfaceFault` now runs the trigger preflight for every
    member, carried bindings or not, so a misplaced trigger is refused before persistence. Every member is
    activated, and activation runs the same preflight. The binding comparison is still skipped for unasserted
    envelopes. The test is renamed to
    `A_misplaced_start_trigger_without_carried_bindings_is_rejected_before_persistence`.
  - **Publish:** `PublishMisplacedStartTriggerRealExtractorEndpointTests` puts the real extractor behind the
    endpoint.
  - **Restore:** `RestoreMisplacedStartTriggerEndpointTests` (US4.6) runs the real publish, unpublish and restore
    handlers. It goes red when the start-entry strategy keeps accepting the placement.
  - **Registration:** `FlowchartStartEntryProviderTests.Feature_RegistersTheProviderForTheFlowchartStructureKind`.
  - **Studio Timer/Cron warning (medium):** not fixed here. Designer-side trigger detection needs a catalog-level
    trigger signal, because the catalog marks CLR triggers as `Action`, and node-anchored publish errors need a
    structured 400. Both are proposed as a follow-up issue.
  - **CodeRabbit:** the spec checklist folder is removed (terminal-spec convention), and a test comment now names
    the first-child strategy.

---

## Phase 8: User Story 4 - Misplaced start triggers are refused at publish (Priority: P1)

**Goal**: A start trigger off the start path fails publish, restore and import with a 400 or the existing failure
channel, and guidance (FR-017 to FR-028).

**Independent Test**: Publish a root Sequence whose child 1 is a start-capable HTTP endpoint. It answers 400, and
nothing is saved. Child 0 publishes.

### Contract and runtime

- [X] T034 [US4] Add `IStructuralStartEntryProvider` (`string StructureKind { get; }`, `ExecutableNode? SelectStartChild(ExecutableNode composite, string triggerNodeId)`) to `src/Workflows/Runtime/Core/Contracts/IStructuralStartEntryProvider.cs`. The XML doc covers three points (research R10):
  - the provider names the child a run started by that trigger begins at;
  - it returns null when no such run can begin inside the composite;
  - it is keyed by `ExecutableActivityStructure.Kind`.
- [X] T035 [US4] In `src/Workflows/Runtime/Core/Exceptions/WorkflowTriggerPreflightException.cs`:
  - add the facet constant `Placement`;
  - add optional `CompositeNodeId` and `CompositeStructureKind` properties, following the existing constructor style;
  - keep every existing constructor and property unchanged.
- [X] T036 [US4] In `src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Triggers/WorkflowTriggerBindingExtractor.cs`:
  - Add a constructor overload that takes `IEnumerable<IStructuralStartEntryProvider>`; the existing constructors pass an empty set.
  - Build the kind map once. A duplicate kind throws `WorkflowTriggerPreflightException` with a non-placement facet, at the first `Evaluate`.
  - After a node's outcome is `Registered`, walk the root-to-node path (track parents in `Flatten`, or compute the path on demand). Throw facet `Placement` on the first composite that does not begin at the next path node, that has no structure, or whose kind has no provider (FR-017, FR-018, FR-022).
  - Use the guidance text from research R11.
  - Update the class XML doc.
- [X] T037 [US4] Make the DI registration resolve the provider set. Check how `IWorkflowTriggerBindingExtractor` is registered (feature in `src/Workflows/Runtime/Elsa.Workflows.Runtime`). If it is constructed through `IEnumerable<IActivityTriggerStimulusProvider>`, add the new enumerable the same way, so the extractor gets every registered provider.
- [X] T038 [P] [US4] Add `src/Activities/Flowchart/Internal/FlowchartStartEntryProvider.cs` (FR-019) and register it in `ActivitiesFlowchartFeature`:
  - `StructureKind = FlowchartActivity.StructureKind`.
  - Use `FlowchartGraph.From(composite)`.
  - Return the trigger when it is a direct child and either is not a connection target or is `SelectStartNode()`.
  - Return null when it is a direct child that is a connection target and not the start node.
  - Otherwise return `SelectStartNode()`.
  - If no inbound helper exists on `FlowchartGraph`, add a small `HasInboundConnection(nodeId)` there.
- [X] T039 [P] [US4] Add `src/Activities/Sequence/Internal/SequenceStartEntryProvider.cs` (FR-020): `StructureKind = SequenceActivity.StructureKind`, returning `SequenceNavigator` over the composite `.SelectFirst()`. Register it in `ActivitiesSequenceFeature`.
- [X] T040 [US4] Apply the outcome of T031. If the reconciler re-evaluates identical, already-active artifacts, ensure that path does not refuse or deactivate them (FR-025). Change only what T031 identified, and add a pinning test in the reconciliation test project.
- [X] T041 [US4] In `src/Workflows/Publishing/Api/Endpoints/WorkflowPublishingExceptionTranslator.cs`, map a `WorkflowTriggerPreflightException` whose facet is `Placement` to 400 with its message. Leave every other facet unmapped (FR-023). Update the class XML doc listing.

### Tests

- [X] T042 [US4] Create `tests/Workflows/Runtime/Tests/WorkflowTriggerPlacementTests.cs`. It uses `WorkflowTriggerBindingExtractor` with a fake stimulus provider and fake start-entry providers for two test kinds (`test.first-child` and `test.none`). Assert facet and node ids only:
  - a root trigger is valid;
  - a trigger at child 0 under `test.first-child` is valid;
  - a trigger at child 1 is refused (`Placement`, with the composite id);
  - a trigger under a structure-less composite is refused;
  - a trigger under an unregistered kind is refused;
  - a trigger two levels deep, valid at each level, is accepted; invalid at the inner level, it is refused naming the inner composite;
  - an `IntentionallyNonStarting` trigger anywhere is never checked;
  - a duplicate provider kind fails with a non-placement facet.
- [X] T043 [US4] Update the structure-less `Elsa.Sequence` roots in `tests/Workflows/Runtime/Tests/WorkflowTriggerBindingExtractorTests.cs` (around l.182, 200, 326), `WorkflowTriggerIndexerTests.cs` (around l.194) and `tests/Workflows/Runtime/Scheduling/Tests/RecurringTriggerScheduleIndexerTests.cs` (around l.79, 176). Give each a structure plus the test start-entry provider, or make the trigger the root, whichever keeps what the test asserts. List each test and its reason under "Revision notes" for the PR (FR-028).
- [X] T044 [P] [US4] Create `tests/Activities/Flowchart/Tests/FlowchartStartEntryProviderTests.cs` covering:
  - a no-inbound direct-child trigger → itself;
  - a start-node trigger with an inbound loop-back → itself;
  - an inbound non-start trigger → null;
  - a deeper trigger → the start node;
  - no `startNodeId` → the `SelectStartNode` fallback.
- [X] T045 [P] [US4] Create `tests/Activities/Sequence/Tests/SequenceStartEntryProviderTests.cs`: returns child 0 for any trigger id; null for an empty Sequence.
- [X] T046 [P] [US4] (FR-027; void if T032 says so) In `src/Activities/Bpmn/Interchange/Services/BpmnDocumentImporter.cs`, author `Literal("CanStartWorkflow", false)` on every nested `BpmnProcess` node. Add a test in `tests/Activities/Bpmn/Interchange/Tests` asserting the nested node carries it and the extractor reports it `IntentionallyNonStarting`.
- [X] T047 [US4] In `tests/Workflows/Publishing/Api/Tests`, add a publish-endpoint test, modelled on `PublishWorkflowTriggerIndexingTests`:
  - publish a draft whose root Sequence has a start trigger at child 1;
  - assert 400, and that no executable, binding or publication was saved;
  - publish the same with the trigger at child 0 and assert success.
- [X] T048 [US4] In `tests/Activities/Http/IntegrationTests`, add `MisplacedStartTriggerEndToEndTests` on `HttpEndpointHostFixture` that drives the real extractor:
  - a Sequence with a start-capable endpoint at child 1 is refused with facet `Placement`, and no binding exists;
  - a root Flowchart with an inbound-connected start-capable endpoint that is not the start node is refused;
  - an outer Flowchart whose start node is an inner Flowchart with a no-inbound trigger child is accepted;
  - the same inner Flowchart reached through a non-start child is refused (US4-4).
  - If the fixture's Sequence nodes lack a structure the provider can read, fix the fixture first (R13).
- [X] T049 [US4] Add a restore test (US4-6, D12) in the Publishing API tests, or wherever `RestorePublicationSlotRequestHandler` is tested:
  - a retired publication whose executable has a misplaced trigger fails to restore with an activation failure;
  - its saved executable is unchanged.
- [X] T050 [US4] Add an import test (US4-7) in the reconciliation test project: an artifact closure with a misplaced start trigger and carried bindings is refused with the "cannot materialize" diagnostic.
- [X] T051 [US4] Run, as whole projects, and compare with the T033 baseline plus the new tests:
  - `Elsa.Workflows.Runtime.Tests`
  - `Elsa.Workflows.Runtime.Scheduling.Tests`
  - `Elsa.Activities.Flowchart.Tests`
  - `Elsa.Activities.Sequence.Tests`
  - `Elsa.Workflows.Publishing.Api.Tests`
  - `Elsa.Activities.Bpmn.Tests`
  - `Elsa.Activities.Bpmn.Interchange.Tests`
  - `Elsa.Activities.Http.IntegrationTests`
  - the reconciliation tests
- [X] T052 [US4] Run the e2e trigger scripts that publish through a live server (`e2e-tests/http/Test-HttpEcho.ps1`, `events/Test-Event.ps1`, `scheduling/Test-Cron.ps1`), as `e2e-tests/README.md` describes, to confirm Sequence child-0 triggers still publish. If the live server can't be run on this machine, say so in the PR evidence instead.

**Checkpoint**: Misplaced start triggers are refused everywhere bindings are computed. Valid placements are
unchanged.

---

## Phase 9: User Story 5 - Studio's misplaced-trigger warning matches the rule (Priority: P2)

- [X] T053 [US5] In `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/flowchartStartNode.ts`, change `findMisplacedStartTriggers` to report a start-capable child that is a connection target and not the start node. A start-capable child with no inbound connection is no longer reported. Update its doc comment (FR-029, D13).
- [X] T054 [US5] Check `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/draftValidation.ts` and the UI that offers "set as start". Update any copy that says the trigger "can never be reached" so it matches the publish guidance (R11). Keep "set as start" for the misplaced case.
- [X] T055 [US5] Update `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/flowchartStartNode.test.ts`, and the `draftValidationSurface` tests if they pin the old shape. Cover US5 scenarios 1–2, and name each changed test and its reason under "Revision notes". Run the Studio client tests for those files.

---

## Phase 10: Revision polish

- [X] T056 [P] Update `EXTENSION_POINTS.md` (FR-026):
  - `src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md` (or the Runtime.Core catalog that owns contracts): the new implementable contract, with signature, registration and Known implementations (Flowchart, Sequence, intra-domain); and the extractor usage text, which gains the placement step.
  - `src/Activities/Flowchart/EXTENSION_POINTS.md` and `src/Activities/Sequence/EXTENSION_POINTS.md`: their implementations.
- [X] T057 [P] Update the "Where a run starts" bullet in `src/Activities/Flowchart/README.md` and add one line to `src/Activities/Sequence/README.md`. Both say where a start trigger may sit, and that other placements are refused at publish.
- [X] T058 Gate evidence, then post it as a PR comment on #2856:
  - all touched test projects as whole projects, with `--list-tests` counts;
  - `bash tools/architecture/restore-ci-project-graph.sh` and the full architecture project;
  - `dotnet restore Elsa.Server.slnx --locked-mode`;
  - maps `-- all` and the diff;
  - a bite-proof: revert the extractor placement step and show T042, T047 and T048 red, then green.
- [X] T059 Set the spec status back to "Implemented — PR for #2850" and tick T031–T059. Then commit, push to `238-flowchart-trigger-start`, update the PR body (US4, US5, D8–D13), and comment on #2850.

---

## Dependencies & Execution Order

- **Phase 1** has no dependencies; T001–T003 run in parallel.
- **Phase 2** (T004) blocks Phase 3 and T022.
- **Phase 3 (US1)**:
  - T005 and T006 come after T004.
  - T007 comes before T008, T009 and T022.
  - T010, T011 and T012–T013 depend only on T005/T006 and touch separate projects, so they can run in parallel.
  - T014 comes last.
- **Phase 4 (US2)** does not depend on Phases 2–3. It can run in parallel with Phase 3 because it touches a different project. T015 → T016 → T017 → T018 → T019/T020 → T021, all in one file.
- **Phase 5 (US3)**: T022 comes after T007 and T005, and touches the same file as T008, so it runs after T008. T023 and T024 come after Phases 3 and 4.
- **Phase 6** comes after Phases 3–5.
- **Phase 7**: T031 blocks T040; T032 blocks T046; T033 comes before any revision code.
- **Phase 8 (US4)**:
  - T034 → T035 → T036 → T037 in order.
  - T038 and T039 (separate modules) run in parallel after T034.
  - T040 comes after T031 and T036.
  - T041 comes after T035.
  - Tests T042–T050 come after their code. T044, T045 and T046 can run in parallel.
  - T051 and T052 come last.
- **Phase 9 (US5)** is independent of Phase 8 (client only). It can run in parallel after T033.
- **Phase 10** comes after Phases 8–9.

## Parallel Example

```text
After T004–T006:
  T010 (Behavioral tests) | T011 (Runtime loop test) | T012→T013 (HTTP e2e)
Independently of Phase 3:
  T015→T021 (ValueFlowValidator, Design project)
```

## Implementation Strategy

1. **MVP (US1)**: Phases 1–3. A multi-trigger Flowchart runs the fired branch. Spec 239 can already rely on the
   runtime behaviour.
2. **US2**: Phase 4, so those workflows also pass validation when a branch reads its trigger's result.
3. **US3 + Polish**: Phases 5–6, which prove no regression, update the docs and open a draft PR after Joey's
   go-ahead.
4. **Placement revision**: Phases 7–10. US4 makes the runtime and the design-time check agree by refusing
   start-trigger placements no run can start from. US5 aligns Studio's warning. The gate evidence comment is posted
   once both land.

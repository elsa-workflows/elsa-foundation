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

- [ ] T001 [P] Run `dotnet test Essentials/tests/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj` on the unchanged tree and record the pass/fail/skip counts in `specs/238-flowchart-trigger-start/tasks.md` under "Baseline" below.
- [ ] T002 [P] Run `dotnet test Essentials/tests/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj --filter "FullyQualifiedName~ValueFlowValidatorTests"` on the unchanged tree and record the counts under "Baseline".
- [ ] T003 [P] Run `dotnet test Essentials/tests/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj` on the unchanged tree and record the counts under "Baseline".

### Baseline

_Filled in by T001–T003._

---

## Phase 2: Foundational

**Purpose**: The one graph method that both the runtime stories and their regression tests depend on.

- [ ] T004 Add `public ExecutableNode? SelectEntryNode(string? triggerNodeId)` to `src/Activities/Flowchart/Internal/FlowchartGraph.cs`, directly below `SelectStartNode()`. When `triggerNodeId` is non-null and `_nodesById` contains it, return that node; otherwise return `SelectStartNode()`. Add an XML doc stating three rules: only a **direct** child counts, because the trigger id is workflow-wide; the method is per run, while `SelectStartNode` stays the static fallback; and `ComputeBackwardEdges` must keep using `SelectStartNode` (research R1, R2).

**Checkpoint**: The Flowchart project builds: `dotnet build Essentials/src/Activities/Flowchart/Elsa.Activities.Flowchart.csproj`.

---

## Phase 3: User Story 1 - A multi-trigger flowchart runs the branch whose trigger fired (Priority: P1) 🎯 MVP

**Goal**: A Flowchart whose direct child is the run's trigger starts at that child (FR-001 to FR-005).

**Independent Test**: Publish a Flowchart with two start-capable HTTP endpoints, fire the second one, and see only
its branch run, with no bookmark left (quickstart §1–§2).

### Implementation

- [ ] T005 [US1] In `src/Activities/Flowchart/Internal/FlowchartExecutionEngine.cs`, replace `graph.SelectStartNode()` with `graph.SelectEntryNode(context.TriggerNodeId)` in `StartAsync` (the `startNode` local), in `OnChildCompletedAsync`'s `CreateInitialState(...)` fallback, and in `OnChildFaultedAsync`'s `CreateInitialState(...)` fallback (FR-001, FR-003). Leave every other line unchanged.
- [ ] T006 [US1] In `src/Activities/Flowchart/Internal/FlowchartGraph.cs` `ComputeBackwardEdges`, change the roots to: the start node (`SelectStartNode()`), then the ids of `Children` whose `Metadata` has `TriggerNodeMetadata.ExecutionTypeKey` equal to `TriggerNodeMetadata.TriggerExecutionType` (ordinal-sorted), then all `_nodesById` keys (ordinal-sorted), with `.Distinct(StringComparer.Ordinal)`. Update the method's doc comment to say why trigger children come second (FR-005, research R2). Add `using Elsa.Workflows.Runtime.Core.Constants;` if needed.

### Tests

- [ ] T007 [US1] In `tests/Activities/Flowchart/Tests/FlowchartRuntimeFixture.cs`, add an `ExecuteAsync(WorkflowExecutable executable, string triggerNodeId)` overload. It calls `WorkflowExecutionHarness.RunAsync(executable, stimulusInput, triggerNodeId)` (`tests/Activities/Testing/WorkflowExecutionHarness.cs:116`), passing an empty JSON object as the stimulus, and returns the same run result as the existing `ExecuteAsync`.
- [ ] T008 [US1] Create `tests/Activities/Flowchart/Tests/FlowchartTriggerStartTests.cs` with the xUnit tests below. Build the graphs like `FlowchartExecutionEngineTests`, with an explicit `startNodeId`, and assert with `AssertRan` / `AssertDidNotRun`:
  - `TriggerChild_IsFirstNode_StartNodeDoesNotRun`: start `a → a2` and second branch `b → b2`; trigger `b`. Then `b` and `b2` ran, and `a` and `a2` did not.
  - `TriggerBranches_JoinSharedNode_SharedNodeRunsOnce`: `a → shared` and `b → shared`; trigger `b`. Then `shared` ran once and `a` did not.
  - `TriggerChild_WithInboundConnection_StartsThereAsRootPath`: `a → b → c`; trigger `b`. Then `b` and `c` ran and `a` did not.
  - `TriggerIsStartNode_BehavesAsToday`: trigger `a`, where `a` is the start node. Then the same nodes ran as on a direct run.
- [ ] T009 [US1] Add `TriggerOnlyLoop_ClassifiesLoopClosingEdgeFromTrigger` to `tests/Activities/Flowchart/Tests/FlowchartCyclicJoinTests.cs`:
  - Graph: start `s` with no outbound connections; trigger-marked `t` (hand-built `ExecutableNode` with `metadata: { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType }`); `t → node-y → node-a → node-y`. The ids are chosen so ordinal order would root the walk at `node-a`.
  - Assert through `FlowchartReachabilityAnalyzer.IsBackwardEdge` that `node-a → node-y` is backward and `node-y → node-a` is not (FR-005, research R2).
  - Add `TriggerReachableFromStart_EdgeClassificationUnchanged`, where `t` is reachable from `s`, asserting the same backward edges as the equivalent unmarked graph.
- [ ] T010 [US1] Create `tests/Activities/Behavioral/Tests/FlowchartTriggerStartBehaviorTests.cs` with real activities, built with `Nodes.Leaf` / `Nodes.Structural` as in `Drives/HttpDrives.cs` (`HttpEndpointDrive`) and `Drives/FlowchartDrive.cs`:
  - `TwoHttpEndpoints_SecondFires_RunsSecondBranchOnly_NoBookmarkLeft`: start node HTTP endpoint A (`CanStartWorkflow=true`) → WriteLine/probe A2; second HTTP endpoint B (`CanStartWorkflow=true`) → probe B2. Run with `harness.RunAsync(..., request, triggerNodeId: "B")`. Assert the workflow completed, B and B2 ran, A and A2 did not, and the bookmark store holds no bookmark for the run (read through `harness.Services`, as `HttpEndpointExecutionTests.BookmarksAsync` does).
  - `CronStartNode_HttpEndpointFires_RunsEndpointBranchOnly`: start node Cron → C2; HTTP endpoint H → H2; trigger `H`. Assert H2 ran and C and C2 did not.
  - `CronStartNode_CronFires_RunsCronBranchOnly`: same graph; run with `RunAsTriggerDeliveryAsync(executable, "C", metadata)`. Assert C2 ran and H and H2 did not, with no bookmark left.
- [ ] T011 [US1] Create `tests/Activities/Runtime/Tests/FlowchartTriggerStartLoopTests.cs`:
  - `ForLoop_FlowchartBody_RestartsAtTriggerEachPass`: a `For` loop with 3 iterations whose body is a Flowchart. The body has start probe `a` and a trigger branch `t → probe-b`, built with a local `NewFlowchartNode` helper like `BreakPropagationExecutionTests.cs:197`.
  - Run with `triggerNodeId: "t"`. Assert `probe-b` ran 3 times and `a` never ran (D3, FR-004).
  - If `t` is a probe leaf, give it a probe node; trigger delivery is only needed for stateful triggers.
- [ ] T012 [US1] Make the HTTP integration host able to publish a Flowchart:
  - Add a `ProjectReference` to `src/Activities/Flowchart/Elsa.Activities.Flowchart.csproj` in `tests/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj`.
  - Add `ActivitiesFlowchartFeature` to the feature list in `tests/Activities/Http/IntegrationTests/HttpEndpointHostFixture.cs` (around l.217-228).
  - Add `PublishTwoHttpEndpointFlowchartWorkflowAsync(string firstPath, string secondPath)`, modelled on `PublishSequencedHttpEndpointsWorkflowAsync` (l.465). It uses `NewHttpEndpointNode(..., canStartWorkflow: true, isTriggerNode: true, nodeId)` for both endpoints, each heading its own branch, and the first endpoint as `startNodeId`.
- [ ] T013 [US1] Create `tests/Activities/Http/IntegrationTests/MultiTriggerFlowchartEndToEndTests.cs` with `SecondEndpointRequest_StartsAtSecondEndpoint_CompletesWithoutBookmarks` (FR-016, SC-001, SC-002):
  - Publish through T012's helper and assert `ListTriggerBindingsAsync` holds two bindings.
  - Send a request to the second path through `Client`.
  - Assert through `SingleWorkflowExecutionAsync()` that the run completed and its trigger node was the second endpoint, and that `ListBookmarksAsync(workflowExecutionId)` is empty.
  - Add `FirstEndpointRequest_StartsAtFirstEndpoint` as the mirror case.
- [ ] T014 [US1] Run `dotnet test` for `Elsa.Activities.Flowchart.Tests`, `Elsa.Activities.Behavioral.Tests --filter "FullyQualifiedName~FlowchartTriggerStart"`, `Elsa.Activities.Runtime.Tests --filter "FullyQualifiedName~FlowchartTriggerStart"` and `Elsa.Activities.Http.IntegrationTests`. Every new test must pass, and the pre-existing counts must match the T001 and T003 baseline.

**Checkpoint**: US1 is shippable on its own. Multi-trigger flowcharts run the fired branch.

---

## Phase 4: User Story 2 - Value flow is validated per entry point (Priority: P1)

**Goal**: The value-flow check treats `startNodeId` plus every direct child with no inbound connection as an entry
point (FR-008 to FR-013).

**Independent Test**: The validator scenarios in quickstart §3 pass without a catalog.

### Implementation

- [ ] T015 [US2] In `src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/Validators/ValueFlowValidator.cs`, change `FlowchartControlFlow.TryCreate(JsonElement? payload)` to `TryCreate(JsonElement? payload, IReadOnlyCollection<string> childNodeIds)`:
  - Compute `entryNodeIds` = `startNodeId` plus each `childNodeIds` element that no edge targets, distinct and ordinal-ordered.
  - Include every entry in `nodeIds`.
  - Pass `entryNodeIds` to the constructor in place of the single `startNodeId`.
  - Keep the early `return null` when `startNodeId` is blank (edge case: no authored start node).
- [ ] T016 [US2] In the same file, update `ValueFlowGraph.Flowchart(NodeInfo container)` to pass the direct children's ids: `_nodes.Where(n => ReferenceEquals(n.Parent, container) && !string.IsNullOrWhiteSpace(n.Node.NodeId)).Select(n => n.Node.NodeId)`.
- [ ] T017 [US2] In the same file, generalise `FlowchartControlFlow.Dominates` to multiple entries (FR-009, FR-011, research R4):
  - `reachable` is the union over entries of `IsReachable(entry, id)`.
  - Each entry's dominator set is `{entry}`; every other reachable node starts at the full `reachable` set.
  - The fixed-point loop iterates reachable nodes that are not entries.
  - With one entry the code must compute exactly what it does today.
- [ ] T018 [US2] In the same file, add `FlowchartControlFlow.EntriesReaching(string node)`, which returns the entries from which `IsReachable(entry, node)` holds. Then extend `ArePotentiallyConcurrent`'s Flowchart branch (FR-010, research R5): the writes are concurrent only when neither reaches the other **and** they are not exclusive. They are exclusive when both entry sets are non-empty and do not overlap.

### Tests

- [ ] T019 [US2] Add these tests to `tests/Workflows/Design/Tests/ValueFlowValidatorTests.cs`, using the existing `Flowchart(...)` JSON helper and `ActivityResult` references. Assert error `Type` values only, never message text (§2.23):
  - `TriggerBranchConsumer_ReadsOwnTriggerResult_IsAccepted`: start `a`; `b` with no inbound edge, `b → c`; `c` reads `b`. No `ValueFlow/UnavailableProducer` (US2-1).
  - `TriggerBranchConsumer_AlsoReachedFromStart_IsRejected`: as above plus `a → c`. `ValueFlow/UnavailableProducer` (US2-2).
  - `WritesOnDifferentTriggerBranches_AreNotConcurrent`: `a → setA` and `b → setB`, both writing variable `v`. No `ValueFlow/ConcurrentWrite` (US2-3).
  - `ForkFromOneEntry_WritesStillConcurrent`: `a → x`, `a → y`, both writing `v`. `ValueFlow/ConcurrentWrite` (US2-4).
  - `MidFlowNodeWithInbound_IsNotEntry_ProducerStillDominates`: `a → p → h → c`; `c` reads `p`. No error (US2-5).
  - `TriggerBranchLoop_CyclicBackEdgeStillDetected`: `b → x → y → x` with no inbound edge to `b`; `x` reads `y`. `ValueFlow/CyclicBackEdge` (FR-011).
- [ ] T020 [US2] Add single-entry regression cases to the same file (FR-012), one each for dominance (accepted and rejected), concurrent writes (fork) and cyclic back-edge, on graphs where every non-start child has an inbound edge. Skip any case an existing test already covers and name that test in a comment.
- [ ] T021 [US2] Run `dotnet test Essentials/tests/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj --filter "FullyQualifiedName~ValueFlowValidatorTests"`. Every pre-existing test must keep the result recorded in T002. If one changes because a disconnected child became an entry point, stop and record the test name and reason for the PR (research R6, §2.21.1) rather than editing the test.

**Checkpoint**: Drafts with several triggers validate and publish.

---

## Phase 5: User Story 3 - Single-start flowcharts behave exactly as before (Priority: P1)

**Goal**: Show that no single-start or direct-run behaviour changed (FR-002, FR-006, SC-004).

**Independent Test**: The regression tests below pass, and the existing suites match the Phase 1 baseline.

- [ ] T022 [P] [US3] Add these tests to `tests/Activities/Flowchart/Tests/FlowchartTriggerStartTests.cs`:
  - `DirectRun_NoTrigger_StartsAtStartNode`.
  - `TriggerIdNotADirectChild_StartsAtStartNode`: trigger id `"elsewhere"`.
  - `NestedFlowchart_TriggerIsInnerChild_OuterStartsAtStartNode_InnerStartsAtTrigger`: the outer start `a → inner`, where the inner Flowchart has start `i1` and trigger child `i2 → i3`; trigger `i2`. Then `a`, `inner`, `i2` and `i3` ran and `i1` did not (D2).
- [ ] T023 [US3] Confirm that no file under `src/Activities/Sequence/`, `src/Activities/Bpmn/` or `src/Activities/ControlFlow/` changed: `git diff --stat origin/main -- Essentials/src/Activities/Sequence Essentials/src/Activities/Bpmn Essentials/src/Activities/ControlFlow` prints nothing (FR-006).
- [ ] T024 [US3] Re-run the T001–T003 suites and confirm that the pre-existing counts equal the baseline (SC-004).

---

## Phase 6: Polish & Cross-Cutting

- [ ] T025 [P] Add a short "Where a run starts" section to `src/Activities/Flowchart/README.md` covering four points (no restatement of glossary terms):
  - A run starts at its trigger node when that node is a direct child.
  - It falls back to the start node otherwise.
  - A nested Flowchart honours only its own children.
  - A loop restarts the Flowchart at the trigger on every pass.
- [ ] T026 [P] Check whether `src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/README.md` describes the value-flow check's start node. If it does, add one sentence on entry points (no-inbound children). If it does not, change nothing.
- [ ] T027 Run `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~ArchitectureGuardTests"` (§E2.2).
- [ ] T028 Run `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` and `git diff --stat docs/`. Only a map that changed for this unit's reason is staged, by explicit path, with `docs/maps/manifest.json`. On Windows, `-- check` reports every map stale because of CRLF, so don't rely on it.
- [ ] T029 Set `**Status**` in `specs/238-flowchart-trigger-start/spec.md` to "Implemented — PR for #2850", and tick T001–T029 here.
- [ ] T030 Commit on `238-flowchart-trigger-start`. Before pushing or opening a PR, ask Joey. The PR is a draft against `main` and carries the gate evidence comment (test counts versus baseline, architecture guard result, the FR-006 diff check).

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

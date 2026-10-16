# Research: Flowchart Starts at the Trigger That Fired

All paths are under `Essentials/`. Verified on origin/main `f4ddd62f2`, 2026-10-16.

## R1. Where the Flowchart chooses its first node

**Decision**: Add one method to `FlowchartGraph`, `SelectEntryNode(string? triggerNodeId)`. It returns the direct
child whose id equals `triggerNodeId`, and otherwise falls back to `SelectStartNode()`. All three engine call
sites use it, passing `context.TriggerNodeId`: `StartAsync`, and the `CreateInitialState` fallbacks in
`OnChildCompletedAsync` and `OnChildFaultedAsync`.

**Rationale**:
- `FlowchartGraph._nodesById` holds exactly the direct children (the `activities` slot), so a lookup there is the
  "direct child" test from D1/D2. Nested nodes are never in it.
- `IRuntimeActivityExecutionContext.TriggerNodeId` is already populated for composite invocations; the BPMN engine
  reads it in its own `StartAsync` (`BpmnExecutionEngine.cs:144`).
- The value is workflow-wide, comes from the persisted seed (`RuntimeWorkflowStateSeed.cs:84`, read back by
  `RuntimeInputBindingStateProjection.ProjectTriggerNodeId`), and is identical on redrive, on resume and on every
  loop pass. D3 and D4 therefore need no extra code.
- The fallbacks run only when no private state is persisted. Using the same method keeps the root scope owner equal
  to the node the run actually started at (FR-003).
- The trigger child then runs with `TriggerNodeId == ExecutableNodeId`. `ActivityStartContext.IsTriggerDelivery` is
  true, so an HTTP endpoint or Event completes with the stimulus instead of suspending (`StatefulActivity.cs:172-195`).

**Alternatives considered**:
- Changing `SelectStartNode()` itself to take the trigger: rejected. `ComputeBackwardEdges` also calls it, and edge
  classification must not depend on the run (R2).
- Reading the trigger from `TriggerMetadata` as BPMN does: not needed. BPMN needs metadata to choose among start
  *elements inside one node*; a Flowchart's trigger is the child node itself.

## R2. Loop-edge classification for branches reached only from a trigger

**Decision**: In `FlowchartGraph.ComputeBackwardEdges`, order the DFS roots as: the start node, then direct children
whose `ExecutableNode.Metadata[TriggerNodeMetadata.ExecutionTypeKey]` equals `TriggerNodeMetadata.TriggerExecutionType`
(ordinal), then all remaining nodes (ordinal). `Distinct` keeps one visit per node, as now.

**Rationale**:
- Backward edges are computed once per executable node and cached through `GetOrAddRoutingStructure`, so they must
  be a pure function of the graph. Using the static trigger marker satisfies that.
- Example of today's defect: `T → A → B → A` with T unreachable from the start node. If B sorts before A, the walk
  starts at B and marks `A → B` as loop-closing; a run from T would then treat its first `A → B` step as a
  loop-back. Rooting at T first marks `B → A`, which is correct.
- A trigger child reachable from the start node is already visited from the start node, so adding it as a root
  changes nothing (FR-005's equality clause). Only regions that today never run get a different, correct
  classification.
- The marker is stamped by `ExecutableNodeCompiler` for every `[TriggerActivity]` node, including mid-flow ones.
  Including mid-flow triggers is harmless for the same reason.

**Alternatives considered**: Recomputing edges per run from the actual entry node. Rejected: breaks caching, and two
runs of one artifact would classify the same edge differently.

## R3. Design-time entry points

**Decision** (D5, revised by Joey 2026-10-16): Entry points are the authored `startNodeId` plus every direct child of
the Flowchart that is the target of no connection. `ValueFlowGraph.Flowchart(container)` passes the container's
direct child ids (the `NodeInfo`s whose parent is the container) to `FlowchartControlFlow.TryCreate`, and the
control flow computes the entry set once.

**Rationale**:
- The catalog marks every CLR activity as `Action`. `ClrAssemblyScanner.cs:226-229` hard-codes it so persisted
  hashes stay valid, and no design facet carries a trigger flag. A catalog-based trigger test would find nothing.
- A child with no inbound connection can only run as the run's trigger (R1), so treating it as an entry point never
  hides a real path. It only over-approximates by including dead nodes.
- No catalog lookup: the validator keeps its single constructor dependency, and only `ValueFlowValidatorTests.cs:146`
  constructs it.

**Alternatives considered**:
- Emit `ExecutionType = Trigger` from the scanner and let the reconciler refresh an ExecutionType-only hash change
  in place, as it does for input `Tier`. Rejected for this unit: it widens scope to reconciliation and changes the
  value the Studio catalog shows.
- No-inbound children filtered by a `CanStartWorkflow` catalog default. Rejected: it adds a catalog dependency for
  little gain.

## R4. Multi-entry dominance

**Decision**: Generalise `FlowchartControlFlow.Dominates` to an implicit virtual root above every entry point:
- `reachable` is the union of nodes reachable from any entry.
- Each entry's dominator set is `{entry}`.
- Every other reachable node starts at the full `reachable` set, and the existing fixed-point iteration skips all
  entries instead of only `startNodeId`.

**Rationale**:
- This is the standard multi-entry dominator construction.
- With one entry it is exactly today's algorithm (FR-012).
- `IsDefinitelyAvailable` (FR-009) and `IsCyclicBackEdgeLookup` (FR-011) both call `Dominates`, so they inherit the
  change.
- An entry with inbound connections (only the start node can have them) keeps `{entry}`, as `startNodeId` does today.

## R5. Exclusive writes across trigger branches

**Decision**: `ArePotentiallyConcurrent` keeps its Flowchart rule ("neither reaches the other") and adds one more
condition: the two writes are not exclusive. `FlowchartControlFlow.EntriesReaching(node)` returns the entries `e`
with `IsReachable(e, node)`. The writes are exclusive when both entry sets are non-empty and do not overlap (FR-010).

**Rationale**:
- One run takes exactly one entry, so writes that only different entries reach never run in the same run.
- With one entry, two writes that the entry reaches share it and stay concurrent. A write the entry does not reach
  has an empty set and stays concurrent. Both match today (FR-012).

## R6. Risk to existing tests

- **Validator**: `ValueFlowValidatorTests` drafts whose Flowcharts contain a disconnected child other than the start
  node would gain an entry point. The suite is run before the change to establish the baseline. Any test whose
  result changes is analysed and named in the PR (constitution §2.21.1), not rewritten silently.
- **Runtime**: no existing test sets a trigger id on a Flowchart run. `HttpEndpointHostFixture` uses Sequence only.
  Existing Flowchart tests therefore exercise only the fallback path.

## R7. Test homes

| Need | Home | Mechanism |
|---|---|---|
| Engine start selection, fallbacks, nested, join, edge roots | `tests/Activities/Flowchart/Tests` | `FlowchartRuntimeFixture` gains a trigger overload over `WorkflowExecutionHarness.RunAsync(executable, stimulusInput, triggerNodeId)`. Backward edges are asserted through `FlowchartReachabilityAnalyzer.IsBackwardEdge`, as in `FlowchartCyclicJoinTests`. Trigger-marked nodes are hand-built with `TriggerNodeMetadata` metadata. |
| HTTP + HTTP and HTTP + Cron with real activities | `tests/Activities/Behavioral/Tests` | Already references Flowchart, Http, Scheduling and Primitives. `Nodes.Leaf`/`Nodes.Structural`, `HttpEndpointDrive` pattern, `RunAsync(..., triggerNodeId:)` / `RunAsTriggerDeliveryAsync`. Bookmarks are read through `harness.Services`. |
| Flowchart in a loop (D3) | `tests/Activities/Runtime/Tests` | `For` with a Flowchart body, as in `BreakPropagationExecutionTests.cs:82`. |
| Publish → stimulus → run (FR-016) | `tests/Activities/Http/IntegrationTests` | `HttpEndpointHostFixture` (publish, real HTTP request, `ListBookmarksAsync`). The test project gains a Flowchart project reference, and the host gains `ActivitiesFlowchartFeature`. |
| Validator | `tests/Workflows/Design/Tests/ValueFlowValidatorTests.cs` | Existing `Flowchart(...)` JSON helper; no catalog needed. |

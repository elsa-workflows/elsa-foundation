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

---

# Revision: start-trigger placement (D8–D13, added 2026-10-16)

Verified on branch `238-flowchart-trigger-start` at `445d734d6`.

## R8. Why a placement rule, and why the start-path form

**Decision**: refuse at publish any start trigger that does not lie on the start path (spec D8).

**Rationale**:
- After US1, the runtime starts a Flowchart at any direct trigger child, including one with inbound connections. The
  value-flow check counts only children without inbound connections as entries.
  - `Event` and `BpmnProcess` default `CanStartWorkflow` to true, and `Timer` and `Cron` always start.
  - So a mid-flow Event, Timer or Cron left at its default would start runs in the middle of a Flowchart, skipping
    upstream work, while validation accepted the draft.
- Start triggers below other composites (a Sequence child > 0, a loop or If body, a nested Flowchart behind a start
  node that did not fire) are indexed today but cannot start a correct run.
- A narrower rule ("root, or a no-inbound direct child of a root Flowchart") was rejected. A census found it would
  refuse a trigger at child 0 of a root Sequence, which runs correctly and is the most common trigger shape:
  - every e2e trigger script (14 scripts);
  - about 11 HTTP integration call sites and 6 runtime unit tests;
  - the Elsa 3 converter's Sequence-root outputs, and Studio's Sequence root option.
- The start-path rule refuses exactly the placements whose runs misbehave and keeps every shape that runs correctly
  today.

**Alternatives considered**:
- No rule (keep US1 only): leaves the runtime and the validator disagreeing.
- One trigger per workflow, with the converter splitting Elsa 3 workflows: contradicts multi-trigger indexing and
  BPMN multi-start, and changes workflow identity and shared state.

## R9. Where the check runs

**Decision**: inside `WorkflowTriggerBindingExtractor.Evaluate`, after each node's provider outcome. It is checked
only for `Registered` outcomes (FR-017).

**Rationale**:
- `Evaluate` is the only place that knows whether a node actually produces start bindings. Authored inputs such as
  `CanStartWorkflow` decide that, and so do the providers.
- Every path that computes bindings goes through it:
  - publish preflight (`WorkflowPublicationPreflightReader.EvaluateAsync`), before `executableStore.SaveAsync`;
  - activation and restore (`WorkflowActivationCoordinator` → `IWorkflowTriggerIndexer.PrepareActivationAsync`);
  - artifact import (`WorkflowArtifactReconciler.TryFindTriggerSurfaceFault`, which already catches
    `WorkflowTriggerPreflightException`).
- The trigger stimulus providers cannot do it: they see one node, and the BPMN provider notes that "root position
  is not recoverable from the published node alone".
- Design-time validators cannot do it either:
  - the catalog marks triggers as `Action`;
  - `IDraftValidator` does not run at publish;
  - whether a node produces bindings depends on provider logic in runtime modules.
- `IExecutableCompilationSource` (Publishing.Core) sees the tree but not binding outcomes, and Flowchart does not
  reference Publishing.

## R10. The start-entry seam

**Decision**: a new strategy contract in `Elsa.Workflows.Runtime.Core/Contracts`:

```csharp
/// Names, for one structural activity kind, the child that a run started by a given trigger begins at.
public interface IStructuralStartEntryProvider
{
    /// The structure kind this provider answers for (ExecutableActivityStructure.Kind).
    string StructureKind { get; }

    /// The child of composite that a run started by triggerNodeId legitimately begins at, or null when no
    /// run started by that trigger can begin inside this composite.
    ExecutableNode? SelectStartChild(ExecutableNode composite, string triggerNodeId);
}
```

**Walk**: for each `Registered` trigger T, the extractor walks the root-to-T path. At each composite C with next
node N, it resolves the provider for `C.Structure?.Kind`. The placement is invalid in any of these cases:
- C has no structure;
- no provider is registered for the kind;
- `SelectStartChild(C, T)` is not N.

The root itself being T is always valid.

**Implementations**:
- **Flowchart** (`FlowchartStartEntryProvider`, `Activities/Flowchart/Internal`), on `FlowchartGraph.From`:
  - returns T when T is a direct child and either has no inbound connection or is `SelectStartNode()`;
  - returns null when T is a direct child with an inbound connection that is not the start node;
  - otherwise returns `SelectStartNode()`.
- **Sequence** (`SequenceStartEntryProvider`, `Activities/Sequence/Internal`): returns `SequenceNavigator.SelectFirst()`.

**Registration and duplicates**:
- Each composite's feature registers its own provider as a multi-registration.
- Two providers for one structure kind are a host configuration error. They fail the preflight with a non-placement
  facet and do not become a 400.

**Pattern**: Strategy (§2.24.2, catalogued). The shape mirrors `IActivityTriggerStimulusProvider` (a multi-registered
strategy the extractor consults) and `IActivityStructureHandler` (keyed by structure kind). It adds no events, so
there is no §2.6.1 fan-in.

**Naming**: `…Provider` follows the Runtime.Core precedent (`IActivityTriggerStimulusProvider`,
`IOperatorActivitySchedulingCapabilityProvider`). It answers a query; it does not act on a context, so it is not a
contributor.

**Public API**:
- `WorkflowTriggerBindingExtractor` is public with a public constructor (used directly by BPMN tests).
- A new overload takes the start-entry providers.
- The existing constructor keeps working with an empty provider set, under which any trigger below a composite is
  refused. This is additive (a minor change).

## R11. Reporting a refusal

**Decision**:
- `WorkflowTriggerPreflightException` gains facet `Placement`. The exception carries the trigger's node id, its
  activity type, the composite's node id and structure kind, and a guidance message.
- `WorkflowPublishingExceptionTranslator` maps a `Placement` failure to 400 with that message. Other facets stay
  unmapped, because they are host configuration failures.

**Guidance text** (one message, shared by publish, restore and import):

> Start trigger '{node}' ({type}) cannot start this workflow: {composite kind} '{composite}' does not begin at it.
> Place it at an entry position: the root activity, the first activity of a Sequence on the start path, or a
> Flowchart activity with no incoming connection. If it should wait mid-flow instead, set CanStartWorkflow to false,
> or use Delay instead of Timer or Cron.

The wording is finalized in implementation, and no test asserts on it (§2.23).

## R12. Publications made before the rule (D12)

- **Restore**: restoring a retired publication re-runs `Evaluate`, so a misplaced trigger now fails the restore
  through the existing activation-failure channel. Its saved executable, instances and bookmarks are untouched.
- **Startup reconciliation**: `WorkflowArtifactReconcilerStartupTask` re-imports mounted artifacts on every shell
  activation. `TryFindTriggerSurfaceFault` re-evaluates a member whenever the envelope carries bindings for it.
  - **Risk**: a mounted artifact that was already active and has a misplaced trigger could be refused on restart.
  - **Plan**: verify whether an identical, already-active artifact reaches this check. If it does, the check must
    not deactivate or re-refuse an already-active identical artifact (FR-025), and the plan pins that with a test.
  - The fallback, if no clean seam exists, is to return to Joey before changing reconciliation.
- **Already-active publications** are not re-evaluated: there is no re-index sweep. Their bindings keep working, and
  the runtime keeps the US1 behaviour for them, including the loop and inbound-connection cases.

## R13. Census: what changes for existing tests and fixtures

| Group | Example | Effect | Action |
|---|---|---|---|
| e2e scripts (14), Sequence child 0 | `e2e-tests/http/Test-HttpEcho.ps1` | Valid | None |
| HTTP integration fixtures, Sequence child 0 | `HttpEndpointHostFixture.PublishSequencedHttpEndpointsWorkflowAsync`, `PublishTriggerSequenceAsync`, `ReferenceWorkflows` | Valid when the Sequence node carries a real Sequence structure and the host registers the Sequence feature | Verify; fix the fixture structure if needed |
| Runtime unit tests with `Elsa.Sequence` roots and no structure | `WorkflowTriggerBindingExtractorTests.cs:182, 200, 326`, `WorkflowTriggerIndexerTests.cs:194`, `RecurringTriggerScheduleIndexerTests.cs:79, 176` | Refused (no structure) | Give them a structure plus a test provider, or make the trigger the root; name the reason in the PR (FR-028) |
| ResponseReplay host fixtures (Sequence child 0) | `ResponseReplayPublicationHost.cs:272`, `*-external-closure.json` | Valid when the Sequence structure is present | Verify |
| BPMN root processes | `BpmnEventStartTriggerTests` | Valid (root) | None |
| BPMN nested processes from the importer | `BpmnDocumentImporter.cs:225-233` sets no `CanStartWorkflow` | Would be `Registered` if they have event starts, then refused | FR-027: author `CanStartWorkflow = false` |
| Spec 238 harness tests (inbound trigger, nested off-path, loop) | `FlowchartTriggerStartTests`, `BreakPropagationExecutionTests` | Harness runs do not extract, so unaffected | Keep as runtime behaviour for artifacts published before the rule; note this in the test comments |
| Studio warning | `flowchartStartNode.ts` `findMisplacedStartTriggers` and its tests | Contradicts D8 | US5 / FR-029 |

## R14. Studio

**Decision** (D13, accepted):
- `findMisplacedStartTriggers` reports a start-capable Flowchart child as misplaced when it has an inbound
  connection and is not the start node. A start-capable child with no inbound connection is no longer reported.
- The "set as start" action stays available for the misplaced case.
- The existing heuristic (a `CanStartWorkflow` literal true) is kept. Timer and Cron, which have no such input, stay
  outside the Studio warning, and publishing still catches them.
- **Files**: `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/flowchartStartNode.ts` and the tests that cover
  it (located in implementation), plus any UI copy that says a trigger "can never be reached".

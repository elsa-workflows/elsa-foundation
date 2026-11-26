# Implementation Plan: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Branch**: `agent/claude/3052-spec1242-bulk-dispatch` | **Spec**: [spec.md](spec.md) | **Issue**: #5833, part of #3052

## Summary

Add an Elsa 4 structural activity, `BulkDispatchWorkflows`, to the DispatchWorkflow activity package. For each item it
schedules one ordinary `DispatchWorkflow` child execution, with its own iteration id and item scope. It runs
`ChildCompleted` or `ChildFaulted` per child while waiting, and completes with `Done`.

Because each child dispatch is its own activity execution, the whole dispatch subsystem is reused unchanged: identity,
stager, resume, cancellation, audit, test-run scope and recovery. The Elsa 3 converter maps its activity to the new one
one to one.

## Technical Context

**Language/Version**: C# / .NET 10; Studio (TypeScript) only through generic slot rendering.

**Primary Dependencies**: none new.

**Storage**: none new. The dispatch records, intents and bookmarks are the existing ones, one set per child.

**Testing**: xunit only. Runtime tests next to the existing DispatchWorkflow runtime tests; converter tests in
`extensions/Elsa3/tests/Mapping`.

**Project Type**: a new activity in `essentials/src/Activities/DispatchWorkflow/Runtime` (plus a structure handler on
the design side), and converter rules.

**Constraints**: pre-stable, so no shims. No author-facing degree, batch or channel input.

## Constitution Check

- **Sanctioned patterns only**: the activity is a structural activity with a structure handler, as `If` and `ForEach`
  are. The converter change is a map entry and existing rule kinds. Pass.
- **No runtime knob for authors**: no degree, batch or channel input. The cap (FR-007) and the waiting degree of 16
  (FR-005) are fixed constants. Pass.
- **Refuse over workaround**: the converter refuses what it cannot map faithfully (spec FR-019), and Elsa 3 defects
  (the shared bookmark counter, the leaking `Input` dictionary) are not reproduced. Pass.
- **ADR needed?** No, with the recommended answer to Q4.
  - Each child keeps "one dispatch per activity execution" (spec 096 FR-012).
  - Per-item iteration ids and scopes follow ADR 0028.
  - The cap of 1,000 and the degree of 16 follow `docs/architecture/large-fan-out.md` decision 1, which bounds
    outstanding children by the degree.
  - All-at-once dispatch with waiting (the alternative in Q4) would depart from decision 1 and needs a decision-register
    entry or an ADR amendment first. The rejected option B would have needed an ADR.

## Design

### 1. Activity

- **Placement**: `BulkDispatchWorkflows : StructuralActivity, IRuntimeStructuralActivity,
  IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler` in
  `essentials/src/Activities/DispatchWorkflow/Runtime/Activities/`, with type key `Elsa.BulkDispatchWorkflows`.
- **Inputs**: the members of spec FR-002. `WorkflowDefinitionId` carries the same `[RefusesSecretBinding(…,
  FixedAtPublish)]` as `DispatchWorkflow`.
- **Visible slots**: `ChildCompleted` and `ChildFaulted`, `[ActivityChildSlot(… Single)]`, both optional.
- **Synthesized dispatch node** (decision, spec FR-004): the node is not authored. `BulkDispatchWorkflowsStructureHandler`
  (an `IActivityStructureHandler` modelled on `IfStructureHandler`) adds it to the executable in
  `CompileExecutableStructure`:
  - node id `{bulkNodeId}.dispatch`, activity type `Elsa.DispatchWorkflow` at the catalog's current version;
  - input bindings copied from the bulk node: `WorkflowDefinitionId` (literal), `Inputs`, `CorrelationId` and
    `CancelChildOnParentCancellation`;
  - `WaitForCompletion` bound to the value the bulk node evaluated once, which it puts in each pass's scope as
    `waitForCompletion` next to `currentItem`.
- **Pinning**: `DispatchPinSource.GetContributionAsync` also matches `Elsa.BulkDispatchWorkflows` nodes in
  `context.RootActivity`. It pins their `WorkflowDefinitionId` and writes `PinnedTargetMetadataKey` for the node id
  `{bulkNodeId}.dispatch`, so `DispatchWorkflow.ValidatePin` finds it at run time. This is the only change to a
  dispatch service.

Research item R-1: confirm that `CompileExecutableStructure` can add a node that the authored structure does not
contain, and that the compiler places it with its bindings and the pin metadata. The fallback is that
`ProjectChildren` synthesizes the node into the authored structure under the same stable id, outside the declared
slots, so publish sees it as authored. Either way the designer shows only the two slots. Record the outcome in the PR.

### 2. Scheduling and completion

- **Start**: evaluate `Items`, refusing a value that is not a collection as `ForEach` does, and evaluate
  `WaitForCompletion`. Check the cap (FR-007). Then schedule the dispatch node, each with an iteration id and a
  `LoopIterationScopeRequest` carrying `currentItem`, `currentIndex` and `waitForCompletion`:
  - without waiting, for every item at once;
  - with waiting, for the first min(N, 16) items (FR-005).
- **Iteration ids**: an item-indexed id derived from the bulk activity execution id with the prefix
  `bulk-dispatch-item`. A small local helper produces it, duplicated from `StructuredExecutionIdentity.Iteration`
  rather than adding a reference from DispatchWorkflow.Runtime to `Elsa.Activities.ControlFlow` ("duplication beats
  dependency"). Spec 1243's neutral-prefix change, if it lands first, can replace the helper.
- **State**: one counter design, kept in the activity's structural state:
  - the item count and the next item index to schedule;
  - the set of ended item indexes. Marking an index twice is a no-op, so a repeated completion callback is safe.

  The activity completes when the set holds every index. A full recount is done only on recovery, grouped by iteration
  id (not by node id, since every pass shares the dispatch node), with `CountProcessedCompletedChildIterationsAsync`
  (FR-024). The callbacks never list children, which would be quadratic, as `Parallel.cs` warns.
- **`OnChildCompletedAsync`**:
  - For the hidden dispatch node, read the outcome.
    - Without waiting (`Dispatched`): mark the pass ended.
    - With waiting: `Completed` schedules `ChildCompleted` in the same iteration scope. `Faulted`, `Cancelled` and
      `DispatchFailed` schedule `ChildFaulted`. With no slot, the pass ends.
  - For a slot child: mark the pass ended.
  - With waiting, each ended pass schedules the next unscheduled item, if any.
  - When every pass has ended, complete with `Done`.
- **`OnChildFaultedAsync`**:
  - A dispatch node fault with waiting and a `ChildFaulted` slot: call `RequestChildFaultAbsorption` and schedule
    `ChildFaulted` in that pass's scope.
  - Any other dispatch node fault, and every slot fault: call `RequestChildFaultIntervention` and return `Defer`, so it
    stays a blocking incident and the pass stays open (spec FR-006, FR-008).
  - The activity implements `IRuntimeAcceptsRescheduledFaultedChild`, so a retry of the faulted child is accepted while
    the activity runs. When the retried child ends, the pass ends as usual.
- **Concurrent same-node scheduling**: no new scheduler (FR-024). Each `ScheduleChildActivity` call gets its own
  activity execution id, and work-item, command and idempotency ids derive from
  `schedule-child:{nodeId}:{childActivityExecutionId}` (`SchedulerWorkItems.cs`). `BpmnProcessRuntime` already schedules
  one bound node, its call-activity `DispatchWorkflow` included, once per instance with an `IterationId` and a
  `LoopIterationScopeRequest`. Use it as the reference. The shared pieces with spec 1243 are
  the iteration-id prefix, the scope request, the processed-completion count and `LoopCollection`. Whichever spec is
  implemented first builds them.

### 3. Converter

- **Map entry**: `Elsa3BuiltInActivityMap.Entries.cs` replaces `NoCounterpart("BulkDispatchWorkflows")` with a
  `Catalog` entry targeting `Elsa.BulkDispatchWorkflows`.
  - Members (spec FR-012–FR-016): reuse the `DispatchMembers` rules for the shared members, plus `Items` (`Carry`,
    `NullUnbound`, a non-null literal refused), `WaitForCompletion` (`DefaultFlip` to true, a case-insensitive
    literal, null as false), and `ChildCompleted`/`ChildFaulted` as `Port` members mapped to the slots.
  - `CorrelationIdFunction`, `DefaultItemInputKey` and `Input` are handled by a node rewrite
    (`Elsa3NodeRewriteKind.BulkDispatchInputs`) that builds the per-item `Inputs` and `CorrelationId` expressions.
  - The surface fixture `elsa3-activity-surface.json` gets the two ports (kind `port`).
- **Item read**: the rewrite registers `get{DefaultItemInputKey}` and `args.<camelKey>` as ambient names for the
  correlation function. `Elsa3ValueFlowLowerer` resolves them to `VariableExpressionParameterBinding(bulkNodeId,
  "currentItem")`, except where a workflow input or variable of that name wins (`ResolveGeneratedAccessor` order),
  as in Elsa 3.
- **Inputs script**: the declared names appear as literals. Item members are reached via
  `Object.entries`/`filter`/`Object.fromEntries`, never `item[name]` (VF-STATE-002). The static `Input` follows the
  #5808 rules (literal or Object) or is embedded (JavaScript). A target outside the import is refused
  (`…-TARGET-UNKNOWN`).
- **Outcomes** (spec FR-017): in `Elsa3FlowchartGraphBuilder.DropDeadEdges`, before `MapPorts` and `CheckJoins`. The
  node rewrite cannot see connections.
- **Child-result reads** (spec FR-019): resolved to `…-CHILD-RESULT-READ` in name resolution for nodes inside the
  converted slots, before the generic missing-name failures.
- **Codes**: 3 errors and 2 warnings in `extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs`. The warnings
  are added to `raisedByRewrites` in `Elsa3ActivityMapCoverageTests`.

### 4. Docs

- A README section for the activity in `essentials/src/Activities/DispatchWorkflow/Runtime/README.md`: cap, batching,
  branches.
- `EXTENSION_POINTS.md` in the same project, if the structure handler is a catalogued extension.
- `extensions/Elsa3/src/Mapping/README.md`.
- `specs/236-elsa3-activity-map/member-map.md` and `contracts/diagnostic-codes.md`.

## Test scope

- **Runtime** (`BulkDispatchWorkflows` tests next to the DispatchWorkflow runtime tests):
  - N children start in one turn, each with distinct dispatch identities;
  - the wait and no-wait completion;
  - each branch routing (Completed, Faulted, Cancelled, DispatchFailed, a dispatch input fault);
  - an empty or null `Items`;
  - the cap fault;
  - a slot fault faults the activity;
  - parent cancellation cancels the waiting children;
  - publish pins the hidden node;
  - recovery recount.
- **Converter** (`Elsa3BulkDispatchConversionTests`): every member rule; each FR-017 and FR-019 row; the item-read
  resolution, including the variable-wins case and `args.item`; the inputs script through publish validation; kept
  slot ids.
- **Coverage tests**: the map coverage and the surface fixture.

## Coordination

- **Spec 1243**: the shared concurrent same-node scheduling (FR-024), and the same `currentItem`/`currentIndex` scope.
- **Spec 1245**: the per-child `Output`, for a later follow-up that exposes it to branches.
- **S-15**: the hand-over of FR-022 after approval.

## Project Structure

```text
essentials/src/Activities/DispatchWorkflow/
├── Runtime/Activities/BulkDispatchWorkflows.cs          (new)
├── Runtime/Constants/…                                  (type key, outcome, cap)
├── Runtime/README.md
└── Design/Services/BulkDispatchWorkflowsStructureHandler.cs (new; placement per R-1)
extensions/Elsa3/src/Mapping/
├── ActivityMap/Elsa3BuiltInActivityMap.Entries.cs
├── ActivityMap/Elsa3NodeRewriteKind.cs
├── ActivityMap/Rewrites/Elsa3BulkDispatchInputsRewrite.cs (new)
├── Conversion/Elsa3FlowchartGraphBuilder.cs             (outcome step)
├── Services/Elsa3ValueFlowLowerer.cs                    (item read, child-result reads)
└── README.md
extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs (5 codes)
```

## Complexity Tracking

None. No constitution gate is exceeded.

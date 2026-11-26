# Implementation Plan: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Branch**: `agent/claude/3052-spec1242-bulk-dispatch` | **Spec**: [spec.md](spec.md) | **Issue**: #5833, part of #3052

## Summary

Add an Elsa 4 structural activity, `BulkDispatchWorkflows`, to the DispatchWorkflow activity package. It has
`Items` and three slots: `Dispatch` (one authored `DispatchWorkflow`), `ChildCompleted` and `ChildFaulted`. For each
item it runs `Dispatch` once, with its own iteration id and item scope, and at most 16 passes are open at once. It runs
the matching slot per child that ends, and completes with `Done`.

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
- **No runtime knob for authors**: no degree, batch or channel input. The cap (FR-007) and the window of 16
  (FR-005) are fixed constants. Pass. This departs from large-fan-out.md decision 1's author input; that is Q4.
- **Refuse over workaround**: the converter refuses what it cannot map faithfully (spec FR-019), and Elsa 3 defects
  (the shared bookmark counter, the leaking `Input` dictionary) are not reproduced. Pass.
- **ADR needed?** No, with the recommended answer to Q4.
  - Each child keeps "one dispatch per activity execution" (spec 096 FR-012).
  - Per-item iteration ids and scopes follow ADR 0028, and BPMN multi-instance already schedules one node per
    iteration.
  - No node is synthesized, so no new pattern.
  - All-at-once dispatch (the alternative in Q4) would drop the bound on outstanding children and needs a
    decision-register entry or an ADR amendment first. The rejected option B would have needed an ADR.

## Design

### 1. Activity

- **Placement**: `BulkDispatchWorkflows : StructuralActivity, IRuntimeStructuralActivity,
  IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler, IRuntimeActivityDescendantFaultHandler` in
  `essentials/src/Activities/DispatchWorkflow/Runtime/Activities/`.
  - Runtime type `Elsa.BulkDispatchWorkflows`.
  - Catalog key `Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows` (spec FR-001).
  - The descendant handler gives a fault deep inside a slot the same intervention-and-defer path as a direct slot
    child (spec FR-008). It does
  not implement `IRuntimeAcceptsRescheduledFaultedChild`: it absorbs some faults, and that contract forbids the marker
  for a parent that does (`IRuntimeStructuralActivity.cs`).
- **Input**: `Items`.
- **Slots**: `Dispatch`, `ChildCompleted`, `ChildFaulted`, all `[ActivityChildSlot(… Single)]`.
- **Structure handler**: `BulkDispatchWorkflowsStructureHandler` projects and replaces the three slots, as
  `IfStructureHandler` does, with two differences from `If`:
  - **`ProjectIterationVariables`** returns `currentItem` and `currentIndex`, typed as `LoopStructureHandler` types
    them. `ScopedVariableResolver` adds them to the descendants' scope only then. Without it, publish refuses every
    `variables.currentItem` read.
  - **`RelateChildren`** answers `Unordered` for every pair, including a child paired with itself. `Dispatch` and both
    slots run concurrently across passes. If's "never overlap" facts would feed spec 266's concurrency checks false
    ordering.
- **`Dispatch` slot check**: an `IDraftValidator` in DispatchWorkflow.Design, modelled on `CompensationTargetValidator`.
  - It looks up the slot child's `ActivityVersionId` in the catalog and reports `BulkDispatch/DispatchSlot` when the
    slot is empty or holds another activity.
  - It needs a reference to Validations.Core. The structure handler's compile step cannot do this check: it sees only
    the version id and cannot emit a coded diagnostic.
- **Initial payload**: `BuildInitialPayload` emits only null or `[]` slots, so a new bulk node starts with `Dispatch`
  empty, and the validator's message guides the author. No Studio-specific code.
- **Pinning**: there is no synthesized node and no `DispatchPinSource` change. The `Dispatch` node is in the executable
  like any authored child, so the existing pin source pins it.

### 2. Scheduling and completion

- **Start**: evaluate `Items`, refusing a value that is not a collection as `ForEach` does. Check the cap (FR-007).
  Schedule `Dispatch` for the first min(N, 16) items, each with an iteration id and a `LoopIterationScopeRequest`
  carrying `currentItem` and `currentIndex`. The scope declares only those two names, typed as `ForEach` types them.
- **Iteration ids**: item-indexed, derived from the bulk activity execution id with the prefix `bulk-dispatch-item`.
  A small local helper produces them, duplicated from `StructuredExecutionIdentity.Iteration` rather than adding a
  reference from DispatchWorkflow.Runtime to `Elsa.Activities.ControlFlow` ("duplication beats dependency").
  Spec 1243's neutral-prefix change, if it lands first, can replace the helper.
- **State**: kept in the activity's structural state, which commits with each evaluation:
  - the item count;
  - the next item index to schedule;
  - the set of ended item indexes.
- **Ending a pass**: an index is marked ended only when its pass has really ended: after a `Dispatch` outcome with no
  slot to run, or after its slot child completes. Only a newly ended index schedules the next item, so a repeated
  callback cannot exceed the window. There is no recount and no new store method.
- **`OnChildCompletedAsync`**:
  - For the `Dispatch` node, route by outcome (FR-006).
  - For a slot child, end the pass.
  - Complete with `Done` when the set holds every index.
- **`OnChildFaultedAsync`**:
  - A `Dispatch` fault with a `ChildFaulted` slot present: `RequestChildFaultAbsorption`, then schedule `ChildFaulted`
    in that pass's scope.
  - Any other fault: `RequestChildFaultIntervention` and `Defer`. Retry goes through `RetryFaultedActivity`, and the
    retried child's completion ends the pass as usual.
- **Concurrent same-node scheduling**: no new scheduler (FR-024). Every `ScheduleChildActivity` call gets its own
  activity execution id (`SchedulerWorkItems.cs`). `BpmnProcessRuntime` already schedules one bound node, its
  call-activity `DispatchWorkflow` included, once per instance with an `IterationId` and a
  `LoopIterationScopeRequest`. Use it as the reference.

### 3. Converter

- **Map entry**: `Elsa3BuiltInActivityMap.Entries.cs` replaces `NoCounterpart("BulkDispatchWorkflows")` with a
  `Catalog` entry targeting the CLR key `Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows`.
  - **Bulk-entry members**: `Items` (`Carry`, `NullUnbound`, a non-null literal refused, FR-014) and
    `ChildCompleted`/`ChildFaulted` as `Port` members mapped to the slots. Every dispatch member is `Structure` (R7) on
    the bulk entry: `WorkflowDefinitionId`, `Input`, `DefaultItemInputKey`, `CorrelationIdFunction`,
    `WaitForCompletion`, `StartNewTrace` and `ChannelName`. The coverage test requires targeted keys to be inputs of
    the entry's own type, and the bulk node has only `Items`.
  - **Behaviour warning**: `ELS3-BEHAVIOUR-BULK-DISPATCH-CAPPED` is an entry-level behaviour warning (spec FR-020).
  - **One rewrite**: `Elsa3NodeRewriteKind.BulkDispatchChild` (the entry takes exactly one) builds the `Dispatch`
    child, an Elsa 4 `DispatchWorkflow` with id `{elsa3NodeId}.dispatch`, marked `SynthesizedFrom` the bulk node, as
    `Elsa3HttpTriggerResponseModeRewrite` does.
    - It sets every member explicitly: `WorkflowDefinitionId` (literal only), `Inputs` (the per-item script),
      `CorrelationId`, `WaitForCompletion` (missing → true, null or valueless → false, case-insensitive literal,
      otherwise carried), and `CancelChildOnParentCancellation` false.
    - It refuses a bound `StartNewTrace` or `ChannelName`.
    - The child is not normalized through the Elsa 3 `DispatchWorkflow` entry, so neither that entry's
      `WaitForCompletion` rule (Carry, NullUnbound) nor its `DispatchInputNames` warning applies.
    - Diagnostics are reported against the bulk node's Elsa 3 properties and paths.
  - **Surface fixture**: `elsa3-activity-surface.json` gets the two ports (kind `port`).
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
- **Codes**: 3 errors and 2 warnings in `extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs`.
  `…-ITEM-INPUTS`, raised by the rewrite, goes in `raisedByRewrites` in `Elsa3ActivityMapCoverageTests`.
  `…-CAPPED` is the entry's behaviour warning.

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
  - a slot fault becomes a blocking incident, the bulk activity defers, and a retry ends the pass;
  - an absorbed `Dispatch` fault runs `ChildFaulted`; without the slot it stays an incident;
  - the window of 16: never more than 16 passes open, including under repeated callbacks;
  - publish refuses an empty or wrong `Dispatch` slot (`BulkDispatch/DispatchSlot`);
  - `variables.currentItem` reads in `Dispatch` and in the slots resolve at publish;
  - a fault inside a `Sequence` in a slot takes the intervention path (descendant fault);
  - parent cancellation cancels the waiting children;
  - publish pins the `Dispatch` node through the unchanged `DispatchPinSource`.
- **Converter** (`Elsa3BulkDispatchConversionTests`): every member rule; each FR-017 and FR-019 row; the item-read
  resolution, including the variable-wins case and `args.item`; the inputs script through publish validation; kept
  slot ids.
- **Coverage tests**: the map coverage and the surface fixture.

## Coordination

- **Spec 1243**: the same `currentItem`/`currentIndex` scope contracts (FR-024). The helpers stay local in 1242.
- **Spec 1245**: the per-child `Output`, for a later follow-up that exposes it to branches.
- **S-15**: the hand-over of FR-022 after approval.

## Project Structure

```text
essentials/src/Activities/DispatchWorkflow/
├── Runtime/Activities/BulkDispatchWorkflows.cs          (new)
├── Runtime/Constants/…                                  (type key, outcome, cap)
├── Runtime/README.md
├── Design/Services/BulkDispatchWorkflowsStructureHandler.cs (new)
└── Design/Services/BulkDispatchSlotValidator.cs        (new, IDraftValidator)
extensions/Elsa3/src/Mapping/
├── ActivityMap/Elsa3BuiltInActivityMap.Entries.cs
├── ActivityMap/Elsa3NodeRewriteKind.cs
├── ActivityMap/Rewrites/Elsa3BulkDispatchChildRewrite.cs (new)
├── Conversion/Elsa3FlowchartGraphBuilder.cs             (outcome step)
├── Services/Elsa3ValueFlowLowerer.cs                    (item read, child-result reads)
└── README.md
extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs (5 codes)
```

## Complexity Tracking

None. No constitution gate is exceeded.

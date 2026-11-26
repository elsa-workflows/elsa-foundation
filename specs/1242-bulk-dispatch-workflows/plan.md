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
- **No runtime knob for authors**: no degree, batch or channel input. The cap is fixed (FR-007). Pass.
- **Refuse over workaround**: the converter refuses what it cannot map faithfully (spec FR-019), and Elsa 3 defects
  (the shared bookmark counter, the leaking `Input` dictionary) are not reproduced. Pass.
- **ADR needed?** No. Each child keeps "one dispatch per activity execution" (spec 096 FR-012). Per-item iteration ids
  and scopes follow ADR 0028. Concurrent passes of one node follow `docs/architecture/large-fan-out.md` (release 1,
  cap 1,000). The rejected option B would have needed one.

## Design

### 1. Activity

- **Placement**: `BulkDispatchWorkflows : StructuralActivity, IRuntimeStructuralActivity,
  IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler` in
  `essentials/src/Activities/DispatchWorkflow/Runtime/Activities/`, with type key `Elsa.BulkDispatchWorkflows`.
- **Inputs**: the members of spec FR-002. `WorkflowDefinitionId` carries the same `[RefusesSecretBinding(…,
  FixedAtPublish)]` as `DispatchWorkflow`.
- **Visible slots**: `ChildCompleted` and `ChildFaulted`, `[ActivityChildSlot(… Single)]`, both optional.
- **Hidden dispatch node**:
  - `BulkDispatchWorkflowsStructureHandler`, an `IActivityStructureHandler` modelled on `IfStructureHandler`, keeps a
    `DispatchWorkflow` node in the authored structure under a slot the facets do not declare. That is the BPMN
    importer's precedent (`BpmnDocumentImporter`, `BpmnElement`).
  - Its inputs are bound from the bulk node's inputs when the structure is compiled.
  - `DispatchPinSource` already walks every descendant `DispatchWorkflow` and pins it.

Research item R-1: confirm that `CompileExecutableStructure` can bind the hidden node's `Inputs` and `CorrelationId` to
the bulk node's expressions, and resolve them per pass inside the iteration scope. Also confirm that `WaitForCompletion`
can be bound to the value the bulk node evaluated once. The candidate is a scope value set by the bulk node, read by
the hidden node. If a binding cannot cross, the hidden node holds the expressions itself and the bulk node's inputs are
a projection of them for the designer. Record the outcome in the PR.

### 2. Scheduling and completion

- **Start**: evaluate `Items`, refusing a value that is not a collection as `ForEach` does, and `WaitForCompletion`.
  Check the cap (FR-007). Then schedule the hidden dispatch node once per item, each with
  `IterationId = StructuredExecutionIdentity.Iteration(aeid, index)` and a `LoopIterationScopeRequest` carrying
  `currentItem` and `currentIndex`, all in the same turn (FR-005).
- **State**: the item count and the number of passes ended, kept in the activity's state. The children are counted
  through `ListAllByParentAsync`, as `Parallel` does, so a recount after recovery is exact.
- **`OnChildCompletedAsync`**:
  - For the hidden dispatch node, read the outcome.
    - Without waiting (`Dispatched`): mark the pass ended.
    - With waiting: `Completed` schedules `ChildCompleted` in the same iteration scope. `Faulted`, `Cancelled` and
      `DispatchFailed` schedule `ChildFaulted`. With no slot, the pass ends.
  - For a slot child: mark the pass ended.
  - When every pass has ended, complete with `Done`.
- **`OnChildFaultedAsync`**: a hidden dispatch node that faults (an input refused at dispatch) is handled like
  `DispatchFailed`, so `ChildFaulted` runs. A slot child that faults faults the bulk activity (FR-008).
- **Concurrent same-node scheduling**: this is the runtime piece shared with spec 1243 (FR-024). If spec 1243 lands
  first, use its scheduler. Otherwise build it here, in the scheduler-facing code, so 1243 can reuse it. Research item
  R-2: confirm that `ScheduleChildActivity` accepts the same child node several times in one turn with distinct
  iteration ids, and that nothing keys on (parent, child node) alone.

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

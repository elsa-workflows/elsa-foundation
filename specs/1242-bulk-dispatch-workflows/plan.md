# Implementation Plan: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Branch**: `agent/claude/3052-spec1242-bulk-dispatch` | **Spec**: [spec.md](spec.md) | **Issue**: #5833, part of #3052

## Summary

The Elsa 3 converter stops refusing `BulkDispatchWorkflows` and lowers it to a loop with a `DispatchWorkflow` body
(spec option B):

- A literal-false wait becomes a `ForEach`. This part does not depend on spec 1243.
- Any other wait becomes spec 1243's parallel ForEach, with the child branches wired from the dispatch's `Completed` and
  `Faulted` outcomes. This part is gated on spec 1243.

Item inputs are built for each pass from the child's declared inputs. `get{DefaultItemInputKey}()` in the correlation
function reads the loop's current item. One new warning and two new errors cover the behaviour differences and the
refusals. The work is limited to the converter, its tests and the docs: the runtime, Studio and the catalog are
unchanged.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: none new. The waiting path needs spec 1243's parallel ForEach in the activity catalog.

**Storage**: none.

**Testing**: xunit only, in `extensions/Elsa3/tests/Mapping` (converter) and the existing Elsa 3 import tests. The
JTest effect is checked by S-15.

**Project Type**: converter rule inside existing services (`extensions/Elsa3/src/Mapping`).

**Constraints**:

- Pre-stable, so there is no shim.
- No new extension point.
- The lowering is a node rewrite. Node rewrites are already a named strategy (framework §2.24.2 Strategy,
  `IElsa3NodeRewrite`), so the lowering introduces no new pattern.

## Constitution Check

- **Sanctioned patterns only**: the lowering is a new `Elsa3NodeRewriteKind` with its own `IElsa3NodeRewrite`, the
  same shape as `ExecuteWorkflowAsDispatch` and `Elsa3LoopVariableReferencesRewrite`. Pass.
- **No runtime knob for authors**: the lowering adds no input to any activity, and spec 1243 has no parallelism
  setting. Pass.
- **Refuse over workaround**: three things are refused with guidance instead of emulated: channels, the child result in
  branches, and `Completed` routes that a run-time wait value would change. The Elsa 3 defects (input leak, shared
  bookmark counter) are not emulated. Pass.
- **ADR needed?** No. Option B uses existing activities and the approved large-fan-out design. Option A, if chosen
  instead, would need an ADR for the multi-child dispatch identity.

## Design

### 1. Map entry and rewrite kind

- In `Elsa3BuiltInActivityMap.Entries.cs`, replace `NoCounterpart("BulkDispatchWorkflows", …)` with a catalog entry
  whose target is `ForEach`, and whose rewrite is a new `Elsa3NodeRewriteKind.BulkDispatchAsLoop`.
  - The rewrite picks between `ForEach` and the parallel ForEach (FR-002, FR-003).
  - The entry's member rules cover the member the loop takes directly: `Items` → `Collection`.
  - The rewrite builds the body dispatch from the remaining members, using the existing `DispatchMembers` rules where
    they apply (`WorkflowDefinitionId`, `Input`, `StartNewTrace`, `ChannelName`, `CancelChildOnParentCancellation`).
- While the parallel ForEach type key is not in the catalog, the rewrite raises `NoCounterpart` for the waiting path,
  with guidance naming spec 1243 (FR-003). The catalog lookup is the gate, so no code change is needed when 1243 lands.

Research item R-1: the rewrite contract runs "after the member rules, before descending into children", on one node.
Before coding, confirm that a rewrite can replace a node with a structural node that has a body child. It can if it
builds the child the way `ExecuteWorkflowAsDispatch` and the Flowchart builder do. If it cannot, the lowering moves into
`Elsa3FlowchartGraphBuilder` next to the existing join and fork rewrites. Record the outcome in the PR.

### 2. Body

- **Without waiting**: the body is the `DispatchWorkflow` node itself, in `ForEach.Body`.
- **With waiting**: the body is a `Flowchart` holding the dispatch.
  - The dispatch's `Completed` and `Faulted` outcomes connect to the converted `ChildCompleted` and `ChildFaulted`
    activities, when they exist.
  - Those activities keep their Elsa 3 ids (FR-004).
  - An empty branch converts to no node.
- **Ids**: the dispatch node and the body flowchart get ids derived from the Elsa 3 node id, for example
  `{id}:dispatch` and `{id}:body`, following the id scheme the converter already uses for synthesized nodes, so that
  conversions are deterministic.

### 3. Per-item inputs (FR-006)

The dispatch's `Inputs` is bound to a generated JavaScript expression over the current item.

- **Target declarations known** (`Elsa3ConversionRequest.DispatchTargetInputs`): the expression is a pick. For each
  declared name `n` it reads the item member whose name matches `n` regardless of case, and emits it under the declared
  spelling `n`; that is the #5808 rule. When the item is not a map, it emits `{[key]: item}`, but only when the target
  declares `key`. Static `Input` entries come first, so the item wins.
- **Target unknown**: the expression spreads the map item, or emits `{[key]: item}`, and raises the existing
  `DispatchInputNames` warning.
- The static `Input` part goes through the existing `Elsa3ValueContract.DispatchInputs` check first, so literal
  undeclared keys stay `ELS3-ACTIVITY-DISPATCH-INPUT-UNDECLARED`.
- The generated script must pass the spec 1223 script rules and the strict-mode rules of spec 262. Converter tests run
  the emitted script through the Elsa 4 evaluator for map, scalar, null and array items.

### 4. Correlation id (FR-008)

- The rewrite applies the JavaScript rewriter to the function with one extra substitution:
  `get{DefaultItemInputKey}()` becomes the current-item read.
  - That is `variables.currentItem` for both `ForEach` and spec 1243's parallel ForEach.
  - The substitution is added to `Elsa3LoopVariableReferencesRewrite`'s table as a context-scoped entry. It is not a
    global host function.
- A literal function becomes a literal `CorrelationId`.

### 5. Outcomes (FR-012)

The rewrite rewires the Elsa 3 node's outgoing connections before the flowchart builder runs:

- `Done` → the loop's `Done`.
- `Canceled` → dropped, with info.
- `Completed` → handled by the wait cases in FR-012.

The check "every target also reached from `Done`" compares the direct target sets.

### 6. Diagnostics (FR-010–FR-013)

New constants in `Elsa3ActivityDiagnosticCodes` (plus `BulkDispatchParallelLimit` only if spec 1243 adds a cap, FR-010):

| Constant | Code | Level |
|---|---|---|
| `BulkDispatchItemInputs` | `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS` | warning |
| `BulkDispatchCompletedRoute` | `ELS3-ACTIVITY-BULK-DISPATCH-COMPLETED-ROUTE` | error |
| `BulkDispatchChildResultRead` | `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ` | error |

- The child-result read check runs on the converted branch expressions. It looks for the `ChildInstanceId` variable,
  and for `getInput`/`args` reads of `WorkflowOutput`, `WorkflowInstanceId`, `WorkflowStatus` and `WorkflowSubStatus`.
- A literal non-collection `Items` is refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`. The converter cannot see a
  run-time value; the documentation describes the difference.

### 7. Docs

- `extensions/Elsa3/src/Mapping/README.md`: the lowering, the member table and the refusals.
- `specs/236-elsa3-activity-map/member-map.md` and `contracts/diagnostic-codes.md`: the BulkDispatchWorkflows row moves
  from R6 to the lowering, and the new codes are added.
- The `DispatchWorkflow` README (`essentials/src/Activities/DispatchWorkflow/`): a "one child per item" section
  (FR-015).

## Test scope

- `Elsa3BulkDispatchLoweringTests` (new):
  - each FR-012 row;
  - the ForEach and parallel ForEach shapes;
  - kept ids;
  - inputs for a known and an unknown target;
  - correlation rewrite with `Item` and with a custom key;
  - the 1243 gate.
- `Elsa3ConversionDiagnosticsTests`: drop BulkDispatchWorkflows from the no-counterpart cases. Keep ParallelForEach
  until 1243.
- `Elsa3ActivityMapCoverageTests`: the new codes are used.
- An end-to-end conversion of a synthetic `acme.*` bulk-dispatch definition modelled on the corpus shape: no wait,
  wait, and an expression wait.

## Coordination

- **Spec 1243** (#5832): its session confirmed on 2026-11-26 that the shape fits: `Collection`,
  `variables.currentItem`, every pass scheduled at start, `Done` after all passes, no cap. A child fault routed inside
  the body is a completed pass. It is not yet approved; if 1243 changes, FR-003, FR-008 and FR-010 follow it.
- **S-15**: the hand-over in FR-016 goes to the S-15 session's notes after approval.

## Project Structure

```text
specs/1242-bulk-dispatch-workflows/
├── spec.md
├── plan.md
├── tasks.md
└── checklists/requirements.md

extensions/Elsa3/src/Mapping/
├── ActivityMap/Elsa3BuiltInActivityMap.Entries.cs     (entry)
├── ActivityMap/Elsa3NodeRewriteKind.cs                (new kind)
├── ActivityMap/Rewrites/Elsa3BulkDispatchAsLoopRewrite.cs (new)
├── Models/Elsa3ActivityDiagnosticCodes.cs             (3 codes)
└── README.md
extensions/Elsa3/tests/Mapping/Tests/Elsa3BulkDispatchLoweringTests.cs (new)
```

## Complexity Tracking

None. No constitution gate is exceeded.

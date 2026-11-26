# Implementation Plan: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Branch**: `agent/claude/3052-spec1242-bulk-dispatch` | **Spec**: [spec.md](spec.md) | **Issue**: #5833, part of #3052

## Summary

The Elsa 3 converter stops refusing `BulkDispatchWorkflows` and lowers it to a loop with a `DispatchWorkflow` body
(spec option B):

- A literal-false wait becomes a `ForEach`. This part does not depend on spec 1243.
- Any other wait becomes spec 1243's parallel ForEach, with the child branches wired from the dispatch's `Completed` and
  `Faulted` outcomes. This part is gated on spec 1243.

Item inputs are built for each pass from the child's declared inputs. `get{DefaultItemInputKey}()` in the correlation
function reads the loop's current item. One new warning, two new errors and one info cover the behaviour differences, the
refusals and what is dropped. The work is limited to the converter, its tests and the docs: the runtime, Studio and the catalog are
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

- **Sanctioned patterns only**: the lowering is a new `Elsa3NodeRewriteKind` with its own `IElsa3NodeRewrite`,
  registered in `Elsa3MappingFeature` next to the seven existing rewrites. Like `Elsa3SwitchValueSynthesisRewrite`
  (`context.AddChild`) and `Elsa3HttpTriggerResponseModeRewrite` (node insertion), it adds nodes. Pass.
- **No runtime knob for authors**: the lowering adds no input to any activity, and spec 1243 has no parallelism
  setting. Pass.
- **Refuse over workaround**: three things are refused with guidance instead of emulated: channels, the child result in
  branches, and `Completed` routes that a run-time wait value would change. The Elsa 3 defects (input leak, shared
  bookmark counter) are not emulated. Pass.
- **ADR needed?** No. Option B uses existing activities and spec 1243's loop. Spec 1243 departs from large-fan-out.md decision 1 (cap and
  degree); that is spec 1243's call, recorded in spec 1242's "What Elsa 4 already has". Option A, if chosen
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
  with guidance naming spec 1243 (FR-003). Phases 1–3 ship with that gate; phase 4 (T014–T018) adds the waiting
  shape once spec 1243 is in the catalog.

Research item R-1 (mostly answered): a rewrite runs "after the member rules, before descending into children", and can
add children through `Elsa3RewriteContext.AddChild` (used by `Elsa3SwitchValueSynthesisRewrite`) or insert nodes
(`Elsa3HttpTriggerResponseModeRewrite`). What is left to confirm in code: that a rewrite can change the node's own
target type, from the Elsa 3 node to `ForEach` or the parallel ForEach, and give it a synthesized body child. If it
cannot, the lowering moves into `Elsa3FlowchartGraphBuilder`. Record the outcome in the PR.

### 2. Body

- **Without waiting**: the body is the `DispatchWorkflow` node itself, in `ForEach.Body`.
- **With waiting**: the body is a `Flowchart` holding the dispatch.
  - The dispatch's `Completed` and `Faulted` outcomes connect to the converted `ChildCompleted` and `ChildFaulted`
    activities, when they exist.
  - Those activities keep their Elsa 3 ids (FR-004).
  - An empty branch converts to no node.
- **Ids**: the dispatch node and the body flowchart get ids derived from the Elsa 3 node id with the converter's
  existing synthesized-id scheme, `{nodeId}.{suffix}` (as `{nodeId}.http-{suffix}` in
  `Elsa3HttpTriggerResponseModeRewrite`): `{nodeId}.bulk-dispatch` and `{nodeId}.bulk-body`. That keeps conversions
  deterministic.

### 3. Per-item inputs (FR-006)

The dispatch's `Inputs` is bound to a generated JavaScript expression over the current item.

- **Target declarations known** (`Elsa3ConversionRequest.DispatchTargetInputs`): the expression is a pick. For each
  declared name `n` it reads the item member whose name matches `n` regardless of case, and emits it under the declared
  spelling `n`; that is the #5808 rule. When the item is not a map, it emits `{[key]: item}`, but only when the target
  declares `key`. Static `Input` entries come first, so the item wins.
- **Target unknown**: the expression spreads the map item, or emits `{[key]: item}`, and raises the existing
  `DispatchInputNames` warning.
- A literal or Object-expression static `Input` goes through the existing `Elsa3ValueContract.DispatchInputs` check
  first, so literal undeclared keys stay `ELS3-ACTIVITY-DISPATCH-INPUT-UNDECLARED`. A JavaScript `Input` is rewritten
  by the existing JavaScript rewrite and embedded as the first operand of the generated object. Any other language is
  refused (FR-006).
- The generated script must pass the spec 1223 script rules and the strict-mode rules of spec 262. Converter tests run
  the emitted script through the Elsa 4 evaluator for map, scalar, null and array items.

### 4. Correlation id (FR-008)

- The rewrite applies the JavaScript rewriter to the function with one extra substitution:
  `get{DefaultItemInputKey}()` becomes `variables.currentItem`, the current-item read of both `ForEach` and spec 1243's
  parallel ForEach.
  - The substitution is new code in the rewrite, not an entry in `Elsa3LoopVariableReferencesRewrite`. That table maps
    an Elsa 3 `CurrentValue` output memory reference to a loop variable; it does no JavaScript substitution, and
    `getItem()` is a Jint argument getter, not a memory reference.
  - Like the existing `variables.x` reads, the emitted read carries a pinned `VariableExpressionParameterBinding` whose
    declaring scope is the loop node (`Elsa3ExpressionRewriter`, the `variables.` parameter bindings). It is not a
    global host function.
- A literal function becomes a literal `CorrelationId`.

### 5. Outcomes (FR-012)

The rewrite rewires the Elsa 3 node's outgoing connections before the flowchart builder runs:

- `Done` → the loop's `Done`.
- `Canceled` → dropped, with info `ELS3-BULK-DISPATCH-UNREACHABLE`.
- `Completed` → handled by the FR-012 rows: dropped when the wait is a literal false or `Done` reaches every target,
  refused otherwise.

The check "every target also reached from `Done`" compares the direct target sets. No duplicate connection is
emitted.

### 6. Diagnostics (FR-010–FR-013)

New constants in `Elsa3ActivityDiagnosticCodes` (plus `BulkDispatchParallelLimit` only if spec 1243 adds a cap, FR-010):

| Constant | Code | Level |
|---|---|---|
| `BulkDispatchItemInputs` | `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS` | warning |
| `BulkDispatchCompletedRoute` | `ELS3-ACTIVITY-BULK-DISPATCH-COMPLETED-ROUTE` | error |
| `BulkDispatchChildResultRead` | `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ` | error |
| `BulkDispatchUnreachable` | `ELS3-BULK-DISPATCH-UNREACHABLE` | info |

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
  - `WaitForCompletion` missing, `null`, a valueless literal, `"False"`, `"true"` and an expression;
  - a static `Input` as literal, Object, JavaScript and another language;
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
├── Elsa3MappingFeature.cs                             (register the rewrite)
└── README.md
extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs (4 codes)
extensions/Elsa3/tests/Mapping/Tests/Elsa3BulkDispatchLoweringTests.cs (new)
```

## Complexity Tracking

None. No constitution gate is exceeded.

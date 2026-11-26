# Implementation Plan: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Branch**: `agent/claude/3052-spec1242-bulk-dispatch` | **Spec**: [spec.md](spec.md) | **Issue**: #5833, part of #3052

## Summary

The Elsa 3 converter stops refusing `BulkDispatchWorkflows` and lowers it to a loop with a `DispatchWorkflow` body
(spec option B):

- A literal-false wait becomes a `ForEach`. This part does not depend on spec 1243.
- Any other wait becomes spec 1243's parallel ForEach, with the child branches wired from the dispatch's `Completed` and
  `Faulted` outcomes. This part is gated on spec 1243.

Item inputs are built for each pass from the child's declared inputs. `get{DefaultItemInputKey}()` in the correlation
function reads the loop's current item. One new warning and two new errors cover the behaviour differences and the refusals;
dropped dead edges reuse `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED`. The work is limited to the converter, its tests and the docs: the runtime, Studio and the catalog are
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
- **No runtime knob for authors**: the lowering adds no input to any activity and sets none. The parallel ForEach's
  `MaxDegreeOfParallelism` is the approved owner decision in large-fan-out.md, owned by spec 1243; the converter leaves
  it at the default. Pass.
- **Refuse over workaround**: three things are refused with guidance instead of emulated: channels, the child result in
  branches, and `Completed` routes that a run-time wait value would change. The Elsa 3 defects (input leak, shared
  bookmark counter) are not emulated. Pass.
- **ADR needed?** No. Option B uses existing activities and spec 1243's loop. Spec 1243 follows large-fan-out.md decision 1 (cap 1,000,
  degree 16). Option A, if chosen
  instead, would need an ADR for the multi-child dispatch identity.

## Design

### 1. Map entry and rewrite kind

- In `Elsa3BuiltInActivityMap.Entries.cs`, replace `NoCounterpart("BulkDispatchWorkflows", …)` with a catalog entry
  whose target is `ForEach`, and whose rewrite is a new `Elsa3NodeRewriteKind.BulkDispatchAsLoop`.
  - The rewrite picks between `ForEach` and the parallel ForEach (FR-002, FR-003).
  - The entry classifies every Elsa 3 member exactly once, as `Every_elsa3_activity_and_member_is_classified_exactly_once`
    requires: `Items` → `Collection` (`NullUnbound`). The other inputs are `Structure` (R7) on the entry, because
    `ForEach` has no such inputs, and the keys are checked against the real catalog by
    `Every_targeted_elsa4_key_slot_and_outcome_exists_in_the_real_catalog`.
  - `ChildCompleted` and `ChildFaulted` are `[Port]` members in 3.6.3, but the Elsa 3 surface fixture
    (`extensions/Elsa3/tests/Mapping/Tests/Fixtures/elsa3-activity-surface.json`) lists only the 8 inputs. The fixture
    gets the two ports (kind `port`), following its "corrected per spec 236 research R-4" precedent, and the entry
    classifies them with `kind: Elsa3MemberKind.Port`, as `ParallelForEach.Body` is.
  - The rewrite applies `LiteralOnly` (`WorkflowDefinitionId`), `DispatchInputs` (`Input`), `Removed` (`StartNewTrace`,
    `ChannelName`) and the synthetic `CancelChildOnParentCancellation` to the body dispatch node itself.
  - The entry's `Outcome` rules map `Done` → `Done`; plan §5 covers `Completed` and `Canceled`.
- While the parallel ForEach type key is not in the catalog, the rewrite raises `NoCounterpart` for the waiting path,
  with guidance naming spec 1243 (FR-003). Phases 1–3 ship with that gate; phase 4 (T014–T018) adds the waiting
  shape once spec 1243 is in the catalog.

Research item R-1 (mostly answered): a rewrite runs "after the member rules, before descending into children", and can
add children through `Elsa3RewriteContext.AddChild` (used by `Elsa3SwitchValueSynthesisRewrite`) or insert nodes
(`Elsa3HttpTriggerResponseModeRewrite`). What is left to confirm in code: that a rewrite can change the node's own
target type, from the Elsa 3 node to `ForEach` or the parallel ForEach, and give it a synthesized body child. If it
cannot, the lowering moves into `Elsa3FlowchartGraphBuilder`. A rewrite cannot see the parent flowchart's connections
(`Elsa3RewriteContext` exposes only its node, properties, children and ancestors), so the outcome handling lives in the
graph builder either way (§5). Record the outcome in the PR.

### 2. Body

- **Loop members**: `Items` → `Collection` with null left unbound (`NullUnbound`, not ForEach's `NullRefused`), and
  any non-null literal refused. `DefaultItemInputKey` null or absent means `Item`.
- **Without waiting**: the body is the `DispatchWorkflow` node itself, in `ForEach.Body`.
- **Missing wait**: the body dispatch gets a literal true `WaitForCompletion` (Elsa 4 defaults to false).
- **Branches without waiting**: the `ChildCompleted`/`ChildFaulted` branches are nested port activities, not
  connections, so the rewrite drops them itself, by not calling `AddChild`, and raises
  `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED` from there.
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
- **Script form**: the declared names and the key appear as string literals. Item members are reached only through
  `Object.entries(item).filter(...).map(...)` and `Object.fromEntries`, as `Elsa3HttpTriggerResponseModeRewrite` does.
  There is no `item[name]`: computed member access by name on anything but `variables`/`args` is workflow-state access
  to the read analysis (`JavaScriptVariableReadAnalyzer`), and publish refuses it with VF-STATE-002.
- **Target unknown**: the expression spreads the map item, or emits `{[key]: item}`, and raises the existing
  `DispatchInputNames` warning.
- A literal or Object-expression static `Input` goes through the existing `Elsa3ValueContract.DispatchInputs` check
  first, so literal undeclared keys stay `ELS3-ACTIVITY-DISPATCH-INPUT-UNDECLARED`. A JavaScript `Input` is rewritten
  by the existing JavaScript rewrite and embedded as the first operand of the generated object. Any other language is
  refused (FR-006).
- The generated script must pass the spec 1223 script rules and the strict-mode rules of spec 262. Converter tests run
  the emitted script through the Elsa 4 evaluator for map, scalar, null and array items.

### 4. Correlation id (FR-008)

- The rewrite registers the lowered loop as a scope whose `currentItem` the body reads, and registers the generated
  accessor `get{DefaultItemInputKey}`, and the `args.<camelKey>` member Elsa 3 also set
  (`JavaScriptExpressionHandler` camelizes every evaluator argument into `args`), as ambient names for the correlation
  function only. Without that, the rewrite refuses `args` with ForbiddenCapability and guidance that points to
  `getVariable`/`getInput`, which would read the wrong value. Name resolution
  (`Elsa3ValueFlowLowerer`, the ambient resolver) resolves that accessor to
  `VariableExpressionParameterBinding(loopNodeId, "currentItem")`, so the JavaScript rewrite emits
  `variables.currentItem` with a pinned binding. A text replacement before the rewrite would not work: a
  pre-substituted `variables.currentItem` resolves as an Elsa 3 variable named `currentItem` (MissingAmbient), and a
  left-over `getItem()` fails as an unknown host function.
  - This is not an entry in `Elsa3LoopVariableReferencesRewrite`. That table maps an Elsa 3 `CurrentValue` output
    memory reference to a loop variable, and `getItem()` is a Jint argument getter, not a memory reference.
  - Registering the scope also makes the existing nested-loop check (`ResolutionDiffers`) fire for a read of an
    enclosing loop's current value inside the lowered body (FR-012).
- A literal function becomes a literal `CorrelationId`.

### 5. Outcomes (FR-012)

The outgoing connections are handled in `Elsa3FlowchartGraphBuilder.Build`, where they are visible: a node rewrite
cannot reach them. The rewrite records the wait classification on the node plan; a bulk-dispatch step in
`DropDeadEdges` (which already raises `DeadConnectionRemoved` as a warning) reads it before `MapPorts`:

- `Done` → the loop's `Done` (the entry's `Outcome` rule).
- `Canceled` → dropped with `DeadConnectionRemoved`, unless it leads to a `WaitAll` join (refused).
- `Completed` → handled by the FR-012 rows: dropped with `DeadConnectionRemoved` when the wait is a literal false;
  dropped silently when `Done` reaches every target; refused when it leads to a `WaitAll` join or to a target `Done`
  does not reach.

Without that step a `Completed` or `Canceled` edge whose source now has only `Done` ends at `GraphOutcomeUnmapped`. The
step runs before `CheckJoins`, so it does the `WaitAll` check itself. For the corpus root (Done, Canceled and Completed into one `WaitAny` join), the join is left with one
inbound edge and collapses (`ConcurrentPair` needs two).

The check "every target also reached from `Done`" compares the direct target sets. No duplicate connection is
emitted.

### 6. Diagnostics (FR-010–FR-013)

New constants in `Elsa3ActivityDiagnosticCodes` (FR-010 reuses spec 1243's `ELS3-BEHAVIOUR-PARALLEL-FOREACH-BOUNDED`):

| Constant | Code | Level |
|---|---|---|
| `BulkDispatchItemInputs` | `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS` | warning |
| `BulkDispatchOutcomeRoute` | `ELS3-ACTIVITY-BULK-DISPATCH-OUTCOME-ROUTE` | error |
| `BulkDispatchChildResultRead` | `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ` | error |

- The child-result read check runs inside name resolution (`Elsa3ValueFlowLowerer`), for nodes inside a lowered
  branch. A generated accessor or `getVariable` read of `ChildInstanceId`, or an input read of `WorkflowOutput`,
  `WorkflowInstanceId`, `WorkflowStatus` or `WorkflowSubStatus`, resolves to `BulkDispatchChildResultRead` before the
  generic missing-variable or unknown-function failure (lines around 1160 and 1255) is reached.
- `ITEM-INPUTS` is added to `raisedByRewrites` in `Elsa3ActivityMapCoverageTests`.
- Any non-null literal `Items` is refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`, because Elsa 3 read it as text. The converter cannot see a
  run-time value; the documentation describes the difference.

### 7. Docs

- `extensions/Elsa3/src/Mapping/README.md`: the lowering, the member table and the refusals.
- `specs/236-elsa3-activity-map/member-map.md` and `contracts/diagnostic-codes.md`: the BulkDispatchWorkflows row moves
  from R6 to the lowering, and the new codes are added.
- `essentials/src/Activities/DispatchWorkflow/Runtime/README.md`: a "one child per item" section (FR-015).

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
  `variables.currentItem`, `Done` after all passes; since then 1243 adopted large-fan-out.md's cap (1,000) and degree
  (16, a `MaxDegreeOfParallelism` input) with `ELS3-BEHAVIOUR-PARALLEL-FOREACH-BOUNDED`. A child fault routed inside
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
extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs (3 codes)
extensions/Elsa3/tests/Mapping/Tests/Elsa3BulkDispatchLoweringTests.cs (new)
```

## Complexity Tracking

None. No constitution gate is exceeded.

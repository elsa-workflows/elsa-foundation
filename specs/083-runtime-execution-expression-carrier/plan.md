# Implementation Plan: Runtime Execution-Time Expression Carrier

**Branch**: `claude/serene-sinoussi-27deb0` | **Date**: 2026-07-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/083-runtime-execution-expression-carrier/spec.md`; governing decision [ADR 0030](../../docs/adr/0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md); source baseline [runtime expression-context source reconciliation](../../docs/reports/runtime-expression-context-source-reconciliation.md).

## Summary

Introduce a **live execution-time expression carrier** — a narrow, purpose-named marker interface (`IExecutionExpressionState`, mirroring `IMaterializationExpressionState`) — implemented by the execution-time `IExpressionExecutionContext` (`SimpleActivityExecutionContext`) and **populated by `WorkflowInvokeActivitySchedulerWorkHandler`** from state it already assembles (projected workflow variables/inputs/activity outputs) plus workflow identity from `WorkflowExecutionState` and the pinned executable. Re-point the five currently-dead JavaScript pre/post-processors onto the carrier (casting the passed `IExpressionExecutionContext`, exactly like `MaterializationAccessorsPreProcessor`) and **strip their `IWorkflowExecutionContext` constructor dependencies**. Keep all four accessor surfaces (identity, named pascalized, exec-time activity-output, variable write-back). **Fold JavaScript variable write-back into the existing durable-value write-back** (`BuildWorkflowScopeWriteBackChanges` -> checkpoint-commit) via the already-threaded `VariableScope`; no second persistence route. **Retire `IWorkflowExecutionContext` as a DI dependency** and remove the unused concrete `WorkflowExecutionContext`. Add the **D4 guardrail test** that enables `JavaScriptWorkflowsRuntimeFeature`, resolves every registered pre/post-processor, and runs a smoke JS evaluation end-to-end.

The write-back fold is the primary hazard; it is de-risked by reusing the exact path (`VariableScope` -> `BuildWorkflowScopeWriteBackChanges`) that already persists non-script workflow-scope variable mutations, so script `setVariable`/`variables.x=` mutations join the same dirty-tracked, checkpoint-atomic durable-value change set with zero new persistence code.

## Technical Context

**Language/Version**: C# / .NET (single `Elsa` assembly, namespaced by domain).

**Primary Dependencies**: `Elsa.Expressions.JavaScript` (Jint), `Elsa.Workflows.Runtime.Core`, `Elsa.Activities.Runtime` (scheduler work handler + durable-value stores). No new external dependency.

**Storage**: Existing `IDurableValueStateStore` (durable values), `IWorkflowExecutionStateStore`, `IActivityExecutionStateStore` via the existing checkpoint-commit path. No new store.

**Testing**: xUnit. Unit tests per re-pointed processor; end-to-end execution tests mirroring `WriteLineVariableInputExpressionExecutionTests` / `SeededVariableEndToEndExecutionTests`; the D4 feature-enablement guardrail.

**Target Platform**: Server runtime (`ElsaRuntimeKinds.Server`).

**Project Type**: Modular workflow engine (single project, domain-namespaced).

**Performance Goals**: No regression. The handler gains at most one workflow-execution-state load on the execution path to source identity (correlation id / name); this load is shared with the existing control-leaf path (reuse the loaded state to avoid a second `FindAsync`). Carrier population is a projection over state the handler already holds.

**Constraints**: Runtime must stay Design-free (constitution §E2.2 / §E2.6). No DI-registered live execution context (ADR 0030 D1). Narrow marker, not a general `TransientProperties` bag (ADR 0030 Q3).

**Scale/Scope**: ~1 new interface, edits to 5 processors + 1 execution context + 1 scheduler work handler; delete 1 interface + 1 concrete + rebase its tests; ~3 test files added/updated. No public API surface widening beyond the new Runtime-internal marker.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Assessment |
|------|-----------|
| **§E2.2 — no `Elsa.Workflows.Runtime.* -> Elsa.Workflows.Design.*` dependency** | **PASS.** The carrier interface, its implementation on `SimpleActivityExecutionContext`, and all re-pointed processors read only runtime state (execution state, durable-value projections, pinned executable). No `Elsa.Workflows.Design.*` reference is introduced. Retiring `IWorkflowExecutionContext` removes coupling, it does not add any. |
| **§E2.6 — artifact-only runtime / executable-always-runs** | **PASS.** Identity is sourced from `WorkflowExecutionState` + the pinned `WorkflowExecutableIdentity`; no authored workflow model is loaded. Consistent with the existing materialization carrier and `WorkflowExecutionContext.WorkflowDefinitionId/VersionId` sourcing. |
| **ADR 0030 D1 — parameter-thread, do not DI-inject** | **PASS.** The carrier is obtained by casting the passed `IExpressionExecutionContext`; it is never DI-registered. `IWorkflowExecutionContext` is retired as a DI dependency. |
| **ADR 0030 Q3 — narrow marker, not general bag** | **PASS.** `IExecutionExpressionState` is a narrow marker mirroring `IMaterializationExpressionState`; no `TransientProperties` bag added to `IExpressionExecutionContext`. |
| **§2.23.1 — feature-class registration test** | **ADDRESSED.** The D4 guardrail is, in effect, the registration test for `JavaScriptWorkflowsRuntimeFeature`: it enables the feature and asserts every registered processor resolves + evaluates. |
| **§2.23.2 — per-implementation unit tests, every branch** | **ADDRESSED.** Each re-pointed processor keeps/gains unit tests covering the carrier-present and carrier-absent (no-op) branches. |
| **§2.23.4 / §2.21.1 — refactoring obligations (existing tests keep passing; deletions need architect approval)** | **FLAGGED.** Retiring `WorkflowExecutionContext` makes `RuntimeWorkflowExecutionContextTests` obsolete (subject removed). See *Complexity Tracking* — removal requires recorded architect approval; the test **objective** (runtime identity/inputs/variables/outputs behavior) is carried forward onto carrier tests. |

**Result**: No new gate required (ADR 0030 confirms). One refactoring-obligation item (test removal) is flagged for architect sign-off in Complexity Tracking. Proceed.

## Project Structure

### Documentation (this feature)

```text
specs/083-runtime-execution-expression-carrier/
├── plan.md              # This file
├── spec.md              # Feature spec (done)
├── research.md          # Phase 0 — decisions & source anchors
├── data-model.md        # Phase 1 — carrier shape & population/write-back flow
├── quickstart.md        # Phase 1 — how a workflow author uses the surfaces
├── contracts/
│   └── execution-expression-carrier.md   # The carrier marker contract
└── checklists/
    └── requirements.md  # Spec quality checklist (done)
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Runtime/Core/
├── Contracts/
│   ├── IExecutionExpressionState.cs          # NEW — narrow execution-time carrier marker
│   ├── IMaterializationExpressionState.cs     # unchanged (reference/mirror)
│   └── IWorkflowExecutionContext.cs           # DELETE (retire DI dependency)
├── WorkflowExecutionContext.cs                # DELETE (unused concrete; not registered)
└── Services/
    └── SimpleActivityExecutionContext.cs      # EDIT — implement IExecutionExpressionState + carrier identity/state

src/Elsa/Workflows/Runtime/JavaScript/
├── JavaScriptWorkflowsRuntimeFeature.cs       # registrations unchanged; processor deps stripped
├── PreProcessors/
│   ├── WorkflowInputFunctionsPreProcessor.cs  # EDIT — read carrier, drop ctor dep
│   ├── WorkflowFunctionsPreProcessor.cs       # EDIT — read carrier, drop ctor dep
│   ├── VariableFunctionsPreProcessor.cs       # EDIT — read carrier, keep IOptions<FeatureOptions>, drop IWorkflowExecutionContext
│   └── ActivityOutputFunctionsPreProcessor.cs # EDIT — read carrier, drop ctor dep
└── PostProcessors/
    └── CopyVariablesToWorkflowContext.cs      # EDIT — read carrier (input-name exclusion via activity context), drop ctor dep

src/Elsa/Activities/Runtime/Services/
└── WorkflowInvokeActivitySchedulerWorkHandler.cs  # EDIT — load workflow state once; populate carrier identity/state; write-back already folds via VariableScope

src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md # EDIT — document the execution-time carrier + IScriptPre/PostProcessor surface
docs/maps/…                                          # regenerate affected generated maps

tests/Elsa/…
├── Activities/Runtime/Tests/
│   ├── JavaScriptRuntimeFeatureEvaluationTests.cs  # NEW — D4 guardrail (enable feature, resolve all, smoke eval)
│   └── RunJavaScriptExecutionAccessorsTests.cs     # NEW — identity/named/output/write-back end-to-end
└── Workflows/Runtime/Tests/
    └── RuntimeWorkflowExecutionContextTests.cs      # REBASE onto carrier or REMOVE (architect approval — see Complexity Tracking)
```

**Structure Decision**: Single-project, domain-namespaced layout (matches the repo). The carrier interface lives in `Elsa.Workflows.Runtime.Core.Contracts` alongside `IMaterializationExpressionState`; it is implemented by the execution-time context in `Elsa.Workflows.Runtime.Core.Services`. Processors stay in `Elsa.Workflows.Runtime.JavaScript.*`. The write-back change is confined to `WorkflowInvokeActivitySchedulerWorkHandler` in `Elsa.Activities.Runtime.Services`.

## Complexity Tracking

| Violation / Obligation | Why Needed | Simpler Alternative Rejected Because |
|------------------------|-----------|--------------------------------------|
| **Remove `RuntimeWorkflowExecutionContextTests`** (subject `WorkflowExecutionContext` deleted) | ADR 0030 D1/Consequences retire `IWorkflowExecutionContext`/`WorkflowExecutionContext`; the concrete type must not return as a service, and keeping a dead type + its tests is the very landmine class the ADR removes. | Keeping the concrete `WorkflowExecutionContext` "just for the tests" preserves the retired mechanism and a re-DI-registration hazard. The test **objective** (identity/inputs/variables/outputs at runtime) is preserved by new carrier tests, so subject/objective continuity per §2.23.4 is met via re-basing. **APPROVED for removal (architect, 2026-07-02)** per §2.21.1 / §2.23.4 — objective re-based onto the new carrier tests. |
| **Handler loads `WorkflowExecutionState` on the execution path** | Identity accessors (`getCorrelationId`, workflow name) need values only held on `WorkflowExecutionState`; the execution-time context currently receives only `WorkflowExecutionId` + `PinnedExecutable`. | Threading correlation/name through the invoke payload would duplicate authoritative state and risk staleness. The load is shared with the existing control-leaf path (`BuildControlLeafWorkflowExecutionStateChangeAsync`), so we reuse the loaded instance rather than reading twice. |

## Open item for architect review (stop point) — RESOLVED 2026-07-02

**Decision (architect, 2026-07-02): generic accessors only, as planned.** The activity-name-qualified pascalized `get{Output}From{Activity}` form is registered only when a runtime activity name is resolvable and otherwise no-ops; capturing runtime activity names in the output projection is deferred as a possible follow-up (a new persistence decision beyond ADR 0030), not taken here.

**Execution-time activity-output accessor granularity.** The dead `ActivityOutputFunctionsPreProcessor` built pascalized, activity-name-qualified accessors `get{Output}From{Activity}` by mapping activity execution ids -> runtime activity names via the old `IWorkflowExecutionContext`. Current runtime state does **not** durably carry the runtime activity *name* for outputs: durable output values are tagged with `OutputName` only (`RuntimeMetadataKeys.OutputName`), and the active-output register keys by `(workflowExecutionId, activityExecutionId, outputName)`. See research.md R4. The plan therefore delivers the execution-time activity-output surface as the **generic name-based accessors** (`getOutput(name)`, `getOutputFrom(activityIdOrName, name)`) sourced from the carrier's activity-output projection — the same surface `MaterializationAccessorsPreProcessor` already provides, now available at execution time. The **activity-name-qualified pascalized form** is registered only for outputs whose activity name is resolvable and otherwise no-ops; fully restoring it would require capturing runtime activity names in the output projection, which is a **new persistence decision beyond ADR 0030** and is flagged here rather than decided. Confirm this scoping at the plan-review stop.

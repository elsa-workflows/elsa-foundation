# Phase 0 Research: Runtime Execution-Time Expression Carrier

All decisions are bounded by [ADR 0030](../../docs/adr/0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md); this file records the source-anchored mechanics, not new design decisions.

## R1 — Where the carrier lives and how it is obtained

**Decision**: The execution-time carrier is a narrow marker interface `IExecutionExpressionState` implemented by `SimpleActivityExecutionContext`, obtained by casting the `IExpressionExecutionContext` parameter passed to each processor.

**Rationale**: `SimpleActivityExecutionContext` is already the execution-time `IExpressionExecutionContext` (`ExpressionExecutionContext => this`, [SimpleActivityExecutionContext.cs:33](../../src/Elsa/Workflows/Runtime/Core/Services/SimpleActivityExecutionContext.cs)) and is what `RunJavaScript` evaluates against (`context.ExpressionExecutionContext`, [Activity.cs:49](../../src/Elsa/Workflows/Runtime/JavaScript/Activities/RunJavaScript/Activity.cs)). It already implements `IScopedVariableProvider`. This mirrors exactly how `MaterializationExpressionExecutionContext` implements `IMaterializationExpressionState` ([RuntimeActivityInputMaterializer.cs:247-318](../../src/Elsa/Workflows/Runtime/Core/Services/RuntimeActivityInputMaterializer.cs)) and how `MaterializationAccessorsPreProcessor` casts to it ([MaterializationAccessorsPreProcessor.cs:29](../../src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/MaterializationAccessorsPreProcessor.cs)).

**Alternatives considered**: A DI-registered live `IWorkflowExecutionContext` — rejected by ADR 0030 D1 (captive-dependency/lifetime hazard; the parameter-carrier already proved sufficient at materialization time).

## R2 — How identity (correlation id, workflow name) reaches the carrier

**Decision**: `WorkflowInvokeActivitySchedulerWorkHandler` loads `WorkflowExecutionState` once on the execution path and passes correlation id + name (plus the already-available `WorkflowExecutionId` and `PinnedExecutable` for definition id/version) into `SimpleActivityExecutionContext`. The loaded state is reused by the existing control-leaf builder to avoid a second read.

**Rationale**: `SimpleActivityExecutionContext` today receives only `workflowExecutionId` + `pinnedExecutable` ([WorkflowInvokeActivitySchedulerWorkHandler.cs:203-212](../../src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs)); correlation id + name live only on `WorkflowExecutionState` ([WorkflowExecutionState.cs](../../src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutionState.cs) — `CorrelationId`, and name in `SystemMetadata[RuntimeMetadataKeys.InstanceName]`). `BuildControlLeafWorkflowExecutionStateChangeAsync` already loads this state conditionally ([:494-497](../../src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs)); moving the load up front and threading the instance keeps it to a single `FindAsync`. Definition version resolution mirrors `WorkflowExecutionContext.ResolveWorkflowDefinitionVersion` (SystemMetadata key or artifact-version major).

**Alternatives considered**: Threading identity through the invoke payload — rejected (duplicates authoritative state, staleness risk).

## R3 — Variable write-back reuses the existing durable-value fold

**Decision**: Script variable mutations (`setVariable`, `set{Name}`, and `variables.x =` copy-back) write through the already-threaded `VariableScope` (via `IScopedVariableProvider` on `SimpleActivityExecutionContext`). `BuildWorkflowScopeWriteBackChanges` then captures them into `durableValueChanges`, folded into the activity checkpoint commit. No new persistence route.

**Rationale**: `VariableFunctionsPreProcessor.SetVariable` already writes through `scopedVariables.TrySetVariableValueByName` ([VariableFunctionsPreProcessor.cs:92](../../src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/VariableFunctionsPreProcessor.cs)); `SimpleActivityExecutionContext.SetVariable`/`TrySetVariableValueByName` route to `VariableScope` ([SimpleActivityExecutionContext.cs:212-255](../../src/Elsa/Workflows/Runtime/Core/Services/SimpleActivityExecutionContext.cs)). The handler already captures workflow-scope mutations post-execution: `BuildWorkflowScopeWriteBackChanges(variableScope, …)` at [:257-258](../../src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs), folded into the change set at [:378-379](../../src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs), and onto suspend/child paths via `CombineDurableValueChanges` ([:292](../../src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs)). Because script mutations land in the same `VariableScope`, they are captured with **no handler change** for the write-back path itself. Dirty-tracking against the start-of-activity projection (`workflowVariables`) is already in place, satisfying the read-only-activity no-change requirement (FR-013 / SC-004).

**Consequence for `CopyVariablesToWorkflowContext`**: The post-processor copies the JS `variables` container back onto the expression context via `expressionContext.SetVariable(...)` ([CopyVariablesToWorkflowContext.cs:37](../../src/Elsa/Workflows/Runtime/JavaScript/PostProcessors/CopyVariablesToWorkflowContext.cs)); on `SimpleActivityExecutionContext` this routes to `VariableScope`, so direct `variables.x =` assignments also land in the write-back set. Its input-name exclusion (`GetInputNames`) is re-pointed to read the activity from the passed context (`expressionContext as IActivityExecutionContext` -> `.Activity` / `.SyntheticProperties` / `IWorkflowActivity.Inputs`) instead of `IWorkflowExecutionContext.GetActivityContextForExpression`.

**Alternatives considered**: A dedicated JS-mutation accumulator folded separately — rejected by ADR 0030 (§Consequences: "MUST NOT introduce a second persistence route").

## R4 — Execution-time activity-output accessor granularity (flagged for architect)

**Finding**: Durable activity outputs are tagged with `OutputName` only (`RuntimeMetadataKeys.OutputName`, [ActivityOutputPublisher.cs:74](../../src/Elsa/Activities/Runtime/Services/ActivityOutputPublisher.cs)); the projection `ProjectActivityOutputValues` is output-name -> value ([RuntimeInputBindingStateProjection.cs:16-17](../../src/Elsa/Workflows/Runtime/Core/Services/RuntimeInputBindingStateProjection.cs)); the active-output register keys by `(workflowExecutionId, activityExecutionId, outputName)` with **no runtime activity name** ([ActiveActivityOutput.cs](../../src/Elsa/Workflows/Runtime/Core/Models/ActiveActivityOutput.cs)). The dead `ActivityOutputFunctionsPreProcessor` needed activity *names* to build `get{Output}From{Activity}` ([ActivityOutputFunctionsPreProcessor.cs:22-32](../../src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/ActivityOutputFunctionsPreProcessor.cs)).

**Decision (bounded, in-scope)**: Deliver the execution-time activity-output surface as the **generic name-based accessors** (`getOutput(name)`, `getOutputFrom(activityIdOrName, name)`) from the carrier's activity-output projection — the same shape `MaterializationAccessorsPreProcessor` registers at materialization time, now available at execution time. Register the **activity-name-qualified pascalized form only when a runtime activity name is resolvable**; otherwise no-op (never throw). Restoring the full pascalized-by-activity-name surface requires capturing runtime activity names in the output projection — a **new persistence decision beyond ADR 0030**, flagged in plan.md for architect confirmation, not decided here.

**Rationale**: Honors ADR 0030 D3 ("execution-time activity-output accessors") using state the runtime can source today, without inventing a persistence change the ADR did not authorize.

## R5 — No-op safety for non-execution contexts

**Decision**: Each re-pointed processor returns early when the passed `IExpressionExecutionContext` is not `IExecutionExpressionState` (mirroring `MaterializationAccessorsPreProcessor`'s `is not IMaterializationExpressionState` guard, [MaterializationAccessorsPreProcessor.cs:29](../../src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/MaterializationAccessorsPreProcessor.cs)).

**Rationale**: The processors are registered globally in `JavaScriptWorkflowsRuntimeFeature`. At materialization time the context is `MaterializationExpressionExecutionContext` (implements `IMaterializationExpressionState`, not the execution marker), so the execution-time processors must not interfere with the working materialization path (SC-006). This also means both carriers can coexist behind the same registered processor set without conflict.

## R6 — Retiring `IWorkflowExecutionContext` and `WorkflowExecutionContext`

**Decision**: Delete `IWorkflowExecutionContext` ([IWorkflowExecutionContext.cs](../../src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutionContext.cs)) and the concrete `WorkflowExecutionContext` ([WorkflowExecutionContext.cs](../../src/Elsa/Workflows/Runtime/Core/WorkflowExecutionContext.cs)) once the five processors no longer reference the interface. Verify no remaining `src/` references before deletion.

**Rationale**: The interface is registered nowhere in `src/` and the concrete is `new`'d only under `tests/` (reconciliation P3). Keeping either preserves the re-DI-registration landmine ADR 0030 removes. `RuntimeWorkflowExecutionContextTests` and two test helpers that `new` the concrete must be rebased/removed (architect approval per Complexity Tracking).

**Alternatives considered**: Renaming `WorkflowExecutionContext` into the carrier — unnecessary; `SimpleActivityExecutionContext` is already the carrier host, so the concrete is pure dead weight.

## R7 — Test infrastructure to mirror

**Decision**: The D4 guardrail and end-to-end tests build a service provider enabling `JavaScriptWorkflowsRuntimeFeature` (rather than registering `MaterializationAccessorsPreProcessor` directly as current tests do). Mirror `WriteLineVariableInputExpressionExecutionTests.BuildServiceProvider` ([tests](../../tests/Elsa/Activities/Runtime/Tests/WriteLineVariableInputExpressionExecutionTests.cs)) for feature wiring (Events/Serialization/Expressions/JavaScript/Jint) plus `new JavaScriptWorkflowsRuntimeFeature().ConfigureServices(services)`.

**Rationale**: The guardrail's whole point (ADR 0030 D4) is that **no test enables the feature today**; the comment in `BuildServiceProvider` ("whose other pre-processors require a live execution context") is the exact gap. The guardrail resolves `IEnumerable<IScriptPreProcessor>` + `IEnumerable<IScriptPostProcessor>` and evaluates a script, asserting no resolution throw.

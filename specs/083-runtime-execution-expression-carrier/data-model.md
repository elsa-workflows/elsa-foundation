# Phase 1 Data Model: Runtime Execution-Time Expression Carrier

No persistent schema changes. This unit adds one in-memory contract and reuses existing durable-value state. "Entities" below are runtime models and their relationships.

## Entity: `IExecutionExpressionState` (new — narrow marker)

The live execution-time expression carrier. Read by the re-pointed processors after casting the passed `IExpressionExecutionContext`. Mirrors `IMaterializationExpressionState` and adds identity.

| Member | Type | Source | Notes |
|--------|------|--------|-------|
| `WorkflowInstanceId` | `string` | `WorkflowExecutionState.WorkflowExecutionId` / `WorkflowExecutionId` on context | Identity funcs (`getWorkflowInstanceId`) |
| `CorrelationId` | `string?` | `WorkflowExecutionState.CorrelationId` | `getCorrelationId` |
| `WorkflowName` | `string?` | `WorkflowExecutionState.SystemMetadata[InstanceName]` | `getWorkflowInstanceName` |
| `WorkflowDefinitionId` | `string` | `PinnedExecutable.DefinitionId` | `getWorkflowDefinitionId` |
| `WorkflowDefinitionVersionId` | `string` | `PinnedExecutable.DefinitionVersionId` | `getWorkflowDefinitionVersionId` |
| `WorkflowDefinitionVersion` | `int` | `PinnedExecutable.ArtifactVersion` major (non-numeric → 0) | `getWorkflowDefinitionVersion`; display identity for scripts, never faults |
| `WorkflowInputs` | `IReadOnlyDictionary<string, object?>` | `RuntimeInputBindingStateProjection.ProjectWorkflowInputs(durableValues)` | `getInput`, named `get{Name}` inputs |
| `WorkflowVariables` | `IReadOnlyDictionary<string, object?>` | `ProjectWorkflowVariables(durableValues)` | fallback source for `getVariable`/named getters when no scope chain |
| `ActivityOutputValues` | `IReadOnlyDictionary<string, object?>` | `ProjectActivityOutputValues(durableValues)` | `getOutput`, `getOutputFrom` (see R4) |

**Design-free invariant**: every source above is Runtime-owned (execution state, durable-value projections, pinned executable). No `Elsa.Workflows.Design.*` type appears.

**Naming**: `IExecutionExpressionState` chosen to parallel `IMaterializationExpressionState` (the two coexisting narrow carriers, ADR 0030 Q3). Final name may be confirmed at implementation; it MUST remain a narrow marker.

## Entity: `SimpleActivityExecutionContext` (edited — carrier host)

Already the execution-time `IExpressionExecutionContext` + `IScopedVariableProvider`. Gains `IExecutionExpressionState` implementation.

- **New constructor inputs** (populated by the handler): correlation id, workflow name, and the three projected dictionaries (variables/inputs/outputs). Definition identity derives from the existing `pinnedExecutable`. All optional/defaulted so existing `new SimpleActivityExecutionContext(...)` call sites (tests) keep compiling; unset -> empty projections and null identity, matching current stub semantics.
- **Variable read/write** continues through `VariableScope` (`IScopedVariableProvider`); `WorkflowVariables` on the carrier serves the no-scope fallback and materialization parity.
- **Existing identity stubs** (`GetWorkflowInstanceId()` etc. that return empty, [SimpleActivityExecutionContext.cs:192-196](../../src/Elsa/Workflows/Runtime/Core/Services/SimpleActivityExecutionContext.cs)) are superseded by carrier-sourced values where the marker is the authoritative surface for the JS processors.

## Relationship: population flow (handler -> carrier -> processors)

```
WorkflowInvokeActivitySchedulerWorkHandler.InvokeActivityAsync
  ├─ durableValues = durableValueStateStore.ListAsync(...)          # already present :152
  ├─ workflowVariables = ProjectWorkflowVariables(durableValues)     # already present :153
  ├─ workflowInputs    = ProjectWorkflowInputs(durableValues)        # already present :167
  ├─ activityOutputs   = ProjectActivityOutputValues(durableValues)  # already present :168
  ├─ workflowState     = workflowExecutionStateStore.FindAsync(...)  # NEW: load once, reuse in control-leaf builder
  ├─ variableScope     = scopeService.BuildScopeAsync(...)           # already present :158
  └─ context = new SimpleActivityExecutionContext(                   # EDIT :203-212 — pass identity + projections
                 …, correlationId, workflowName, workflowVariables, workflowInputs, activityOutputs, variableScope)
        │
        └─ activity.ExecuteAsync(context)                            # RunJavaScript evaluates against context.ExpressionExecutionContext (== context)
              └─ IJavaScriptEvaluator -> PreProcessScript resolves IEnumerable<IScriptPreProcessor>
                    ├─ MaterializationAccessorsPreProcessor  -> no-op (context is not IMaterializationExpressionState)
                    ├─ WorkflowFunctionsPreProcessor         -> reads IExecutionExpressionState (identity, getInput, getOutputFrom)
                    ├─ WorkflowInputFunctionsPreProcessor    -> named input accessors from carrier
                    ├─ VariableFunctionsPreProcessor         -> named var accessors + get/setVariable via VariableScope
                    ├─ ActivityOutputFunctionsPreProcessor   -> output accessors from carrier (R4)
                    └─ WorkflowVariablesContextPreProcessor  -> unchanged (no IWorkflowExecutionContext dep)
              └─ CopyVariablesToWorkflowContext (post)       -> copies variables container back via context.SetVariable -> VariableScope
```

## Relationship: write-back flow (carrier mutation -> durable value -> checkpoint)

```
script mutates variable (setVariable / set{Name} / variables.x=)
  -> VariableFunctionsPreProcessor / CopyVariablesToWorkflowContext
     -> SimpleActivityExecutionContext.(Try)SetVariable
        -> VariableScope.TrySetValueByName            # already the workflow-scope write target
  -> after activity.ExecuteAsync:
     BuildWorkflowScopeWriteBackChanges(variableScope, …, workflowVariables[start-of-activity], …)   # :257-258, dirty-tracked
        -> workflowVariableWriteBackChanges (empty if nothing changed)
  -> completion path : durableValueChanges = durableValueChanges.Concat(workflowVariableWriteBackChanges)  # :378-379
     suspend/child path: CombineDurableValueChanges(...) rides bookmark/child checkpoint                   # :292
  -> RuntimeCheckpointCommit.StateChanges.durableValues  (atomic with the activity checkpoint)
```

No new state type, store, or persistence route. Script mutations are indistinguishable from non-script workflow-scope mutations at the durable-value layer, satisfying FR-012.

## State/behavior rules

- **Dirty tracking**: read-only script -> `BuildWorkflowScopeWriteBackChanges` returns empty -> zero durable-value change (SC-004, FR-013).
- **No-op guard**: non-execution contexts (materialization) skip the execution processors (R5, FR-006, SC-006).
- **Deterministic missing values**: value accessors return null/undefined for absent names; the retired 064 contract's deterministic `InvalidOperationException` behavior (spec 064 FR-007) is preserved where a hard failure is the specified surface (FR-013 / spec FR-017).

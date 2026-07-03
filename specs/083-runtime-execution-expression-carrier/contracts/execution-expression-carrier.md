# Contract: Execution-Time Expression Carrier

**Kind**: Runtime-internal marker interface (not a DI service, not a public cross-domain contract). Parameter-threaded via `IExpressionExecutionContext` (ADR 0030 D1).

## Interface (illustrative — final naming settled at implementation)

```csharp
namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Exposes the live, post-seed execution-time workflow state (identity, inputs, variables, prior
/// activity outputs) that a mid-activity expression may reference. The execution-time
/// IExpressionExecutionContext implements this so language-specific pre/post-processors can surface
/// getWorkflowInstanceId(), named accessors, output accessors, and variable write-back without a
/// DI-registered live workflow execution context. Mirrors IMaterializationExpressionState for the
/// execution (rather than materialization) evaluation point.
/// </summary>
public interface IExecutionExpressionState
{
    string WorkflowInstanceId { get; }
    string? CorrelationId { get; }
    string? WorkflowName { get; }
    string WorkflowDefinitionId { get; }
    string WorkflowDefinitionVersionId { get; }
    int WorkflowDefinitionVersion { get; }

    IReadOnlyDictionary<string, object?> WorkflowInputs { get; }
    IReadOnlyDictionary<string, object?> WorkflowVariables { get; }
    IReadOnlyDictionary<string, object?> ActivityOutputValues { get; }
}
```

## Consumer contract (processors)

Each re-pointed processor MUST:

1. Take **no** `IWorkflowExecutionContext` (or other live-execution-service) constructor dependency. `VariableFunctionsPreProcessor` retains only `IOptions<FeatureOptions>`.
2. Cast the passed `IExpressionExecutionContext` to `IExecutionExpressionState`; return early (no-op) when the cast fails (non-execution context).
3. Read identity/inputs/variables/outputs from the carrier; register JavaScript functions using the existing `WorkflowFunctionNames` constants so the editor's declaration contributors stay in sync.
4. For variable **writes**, route through the context's `IScopedVariableProvider` / `SetVariable` surface (which lands in `VariableScope`), never through a bespoke persistence call.

## Producer contract (scheduler work handler)

`WorkflowInvokeActivitySchedulerWorkHandler` MUST populate the carrier from:
- `WorkflowExecutionState` (correlation id, name, version metadata) — loaded once, reused by the control-leaf builder.
- `PinnedExecutable` (definition id / version id / artifact version).
- `RuntimeInputBindingStateProjection` projections already computed for the resolution context (variables/inputs/outputs).

The handler MUST NOT register the carrier in DI, and MUST keep JavaScript variable write-back flowing through the existing `BuildWorkflowScopeWriteBackChanges` durable-value fold.

## Function surface preserved (from `WorkflowFunctionNames`)

`getWorkflowInstanceId`, `getCorrelationId`, `setCorrelationId`, `getWorkflowInstanceName`, `setWorkflowInstanceName`, `getWorkflowDefinitionId`, `getWorkflowDefinitionVersionId`, `getWorkflowDefinitionVersion`, `getInput` + `get{Name}` inputs, `getVariable`/`setVariable` + `get{Name}`/`set{Name}` variables, `getOutputFrom`/`getOutput`, `getLastResult`. `setCorrelationId`/`setWorkflowInstanceName` continue to route through the existing control-leaf intent path on the context, not the carrier.

## Invariants

- **Design-free** (§E2.2 / §E2.6): no `Elsa.Workflows.Design.*` reference.
- **Narrow marker** (Q3): no `TransientProperties` bag added to `IExpressionExecutionContext`.
- **Single persistence route** (FR-012): variable write-back reuses the checkpoint-commit durable-value fold only.

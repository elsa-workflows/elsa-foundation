# NotImplemented Classification

Status: point-in-time codebase reality check for `NotImplementedException`, placeholder, and intentionally deferred execution signals.

## Purpose

Classify the concrete `NotImplementedException` and nearby placeholder signals identified by [test maturity and weak implementation report](test-maturity-and-weak-implementation-report.md). This report separates intentional deferral, test-only stubs, unreachable placeholders, resolved fixes, and likely code drift so the next work unit can be scoped safely.

## Inputs Reviewed

- Source search for `NotImplementedException`, `TODO`, `DEFERRED`, `deferred`, `placeholder`, `stub`, `not implemented`, `future`, and `follow-up`.
- [Test maturity and weak implementation report](test-maturity-and-weak-implementation-report.md)
- [Test map](../maps/test-map.md)
- `src/Elsa/Workflows/Runtime/Core/WorkflowExecutionContext.cs`
- `src/Elsa/Workflows/Design/Core/WorkflowDesignContext.cs`
- `src/Elsa/Expressions/Services/VariableExpressionDescriptor.cs`
- `src/Elsa/Http/Services/MultiDownloadableContentHandler.cs`
- `src/Elsa/Workflows/Runtime/JavaScript/Activities/RunJavaScript/TestClasses/ScriptExecutionContext.cs`
- Related feature registration and consumer code.

## Findings

| Finding | Classification | Evidence | Risk | Recommended next action |
|---|---|---|---|---|
| `WorkflowExecutionContext` is a real, non-stub implementation, but is never constructed or DI-registered in production. | Superseded classification — implemented contract, unwired execution-context lifetime | Correction of the "throws for every member" claim below: `WorkflowExecutionContext` (`src/Elsa/Workflows/Runtime/Core/WorkflowExecutionContext.cs`) now has real logic — constructor seeds inputs/variables/outputs, `GetVariable`/`SetVariable`/`GetInput`/`GetOutput`/`GetWorkflowInputs` all work. `new WorkflowExecutionContext(...)` appears nowhere outside `tests/`, no `AddScoped`/`AddSingleton`/`AddTransient` registration exists anywhere in `src/`, and the real per-work-item DI scope in `WorkflowInvokeActivitySchedulerWorkHandler.cs:77` builds a `SimpleActivityExecutionContext` piecemeal from stores instead of constructing or registering one. Five `IScriptPreProcessor` implementations under `src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/` (`WorkflowInputFunctionsPreProcessor`, `WorkflowFunctionsPreProcessor`, `VariableFunctionsPreProcessor`, `ActivityOutputFunctionsPreProcessor`) plus `CopyVariablesToWorkflowContext` in `PostProcessors/` all take `IWorkflowExecutionContext` via constructor injection, so they are effectively dead code at runtime — DI cannot satisfy the dependency. The one sibling that avoids this, `MaterializationAccessorsPreProcessor`, takes nothing via constructor and instead derives everything from the `IExpressionExecutionContext` parameter already passed into `PreProcess(...)`, casting to `IMaterializationExpressionState`. Non-JS expression evaluators (e.g. `LiquidExpressionHandler`) never depend on `IWorkflowExecutionContext` at all, confirming this dependency is specific to — and unfinished in — the JS runtime pre-processor layer. See [Elsa Core runtime expression-context wiring analysis](elsa-core-runtime-expression-context-wiring-analysis.md) for the upstream comparison and design options. | High once runtime/JS expression work starts; currently low because nothing in production exercises the five broken pre-processors successfully today (they would throw at first resolution). Analogous to the `WorkflowDesignContext` row below — both are injectable-ambient-context ideas that were never wired. | Keep attached to the Runtime Execution Seam bucket's "workflow execution context lifetime, DI scope, and concurrency model" objective. Do not patch member-by-member or wire DI registration as a drive-by fix; the fix requires deciding the execution-context lifetime/shape first (see the linked analysis for options). |
| `WorkflowDesignContext` throws for both exposed members. | Incomplete design-context service / unwired design-scope idea | `WorkflowDesignContext` implements `IWorkflowDesignContext`, but no registration or factory implementation was found. Workflow design JavaScript contributors depend on `IWorkflowDesignContext`. The original intent was an injectable design-time context, analogous to `IWorkflowExecutionContext`, so draft/activity values would not have to be passed through every method contract. It was not wired because the full design endpoint/UI suite is not defined yet. See the `WorkflowExecutionContext` row above — the runtime side has the same unwired-ambient-context pattern, and the design and runtime cases should likely be resolved with a consistent answer. | Design-time JavaScript declaration contributors can fail at DI or fail when reading draft/activity context unless an external host supplies the context. The ambient-context assumption may still be useful, but it needs validation against the eventual design endpoint/UI model. | Keep this classification and the source comments as the safeguard. Revisit the design-context ownership, scoping, and factory-vs-direct-context question when full design-suite implementation starts; do not promote it into the current Elsa Foundation Operating Model bucket. |
| `VariableExpressionDescriptor` formerly threw for `HandlerFactory` and `Properties`. | Resolved active code drift | `DefaultExpressionDescriptorProvider` always yields `VariableExpressionDescriptor`; `ExpressionEvaluator` calls `HandlerFactory` for the selected descriptor. Fixed by wiring `VariableExpressionHandler` and empty descriptor properties, with a regression test in `Elsa.Activities.Runtime.Tests`. | Resolved for the default variable expression path. | Keep the regression test; revisit only if variable descriptor metadata becomes richer. |
| `MultiDownloadableContentHandler` throws for `Priority`. | Unregistered placeholder / incomplete feature | The class implements `IDownloadableContentHandler`, but `HttpFeature` does not register it. If registered later, `Priority` throws. | Enumerable downloadable content is not handled by the feature today; future registration would introduce an executable failure path. | Decide whether enumerable downloadables are in scope. If yes, register the handler with a concrete priority and tests; if no, delete or quarantine the placeholder. |
| `ScriptExecutionContext` in `RunJavaScript/TestClasses` throws for some `IActivityExecutionContext` members. | Demo/dev endpoint fake context, not production runtime context | `Endpoint` creates this context inside a production source project; the current activity path does not call the throwing members, but the type is under a `TestClasses` namespace in `src`. | The endpoint can appear more production-ready than it is, and future activity behavior may hit unsupported members. | Quarantine the endpoint/context as dev/test-only or replace it with the future runtime execution context after the runtime seam is specified. |
| Test-project `NotImplementedException` members appear in local stub classes. | Test-only stubs | Throws are in test helper implementations for narrow interfaces. | Low; tests use only the implemented members. | No immediate action unless a test starts exercising the stubbed member. |
| `WorkflowDefinitionActivity.Execute` throws `NotSupportedException`. | Explicit construct-only deferral | Source comment and Unit 006 scope say workflow-as-activity execution is deferred. | Medium; consumers may mistake construction readiness for execution readiness. | Keep attached to the consumer/pinning/runtime execution unit. |

## Suggested Priority

1. Do not promote Workflows Design JavaScript/context into the current Elsa Foundation Operating Model bucket. Keep the classification as the safeguard and revisit it when full design-suite implementation starts.
2. Keep `MultiDownloadableContentHandler` as recorded codebase evidence until a code-change or HTTP implementation bucket is explicitly selected.
3. Keep the JavaScript activity demo endpoint/context finding as recorded codebase evidence until a code-change or runtime/demo cleanup bucket is explicitly selected.
4. Leave `WorkflowExecutionContext` and `WorkflowDefinitionActivity.Execute` attached to runtime execution planning. The `WorkflowExecutionContext` finding now includes the five dead JS pre-processors as concrete downstream evidence; see [Elsa Core runtime expression-context wiring analysis](elsa-core-runtime-expression-context-wiring-analysis.md).

## What This Report Does Not Do

- It does not implement the runtime execution seam.
- It does not claim behavioral coverage for the affected projects.
- It does not require immediate removal of every placeholder.
- It does not convert test-only stubs into production findings.

# NotImplemented Classification

Status: point-in-time codebase reality check for `NotImplementedException`, placeholder, and intentionally deferred execution signals.

## Purpose

Classify the concrete `NotImplementedException` and nearby placeholder signals identified by [test maturity and weak implementation report](test-maturity-and-weak-implementation-report.md). This report does not implement fixes; it separates intentional deferral, test-only stubs, unreachable placeholders, and likely code drift so the next work unit can be scoped safely.

## Inputs Reviewed

- Source search for `NotImplementedException`, `TODO`, `DEFERRED`, `deferred`, `placeholder`, `stub`, `not implemented`, `future`, and `follow-up`.
- [Test maturity and weak implementation report](test-maturity-and-weak-implementation-report.md)
- [Test map](../maps/test-map.md)
- `src/Elsa.Workflows.Runtime.Core/WorkflowExecutionContext.cs`
- `src/Elsa.Workflows.Design.Core/WorkflowDesignContext.cs`
- `src/Elsa.Expressions/Services/VariableExpressionDescriptor.cs`
- `src/Elsa.Http/Services/MultiDownloadableContentHandler.cs`
- `src/Elsa.Workflows.Runtime.JavaScript/Activities/RunJavaScript/TestClasses/ScriptExecutionContext.cs`
- Related feature registration and consumer code.

## Findings

| Finding | Classification | Evidence | Risk | Recommended next action |
|---|---|---|---|---|
| `WorkflowExecutionContext` throws for every member. | Intentional deferred runtime contract / known stub | Elsa constitution and the runtime execution handoff already classify Runtime as deferred; `Elsa.Workflows.Runtime.Core` has no direct test-project reference. | High once runtime work starts; acceptable only while Runtime execution remains deferred. | Keep attached to the Runtime execution seam work unit. Do not patch member-by-member before the executable artifact and execution-context model are specified. |
| `WorkflowDesignContext` throws for both exposed members. | Incomplete design-context service / unwired design-scope idea | `WorkflowDesignContext` implements `IWorkflowDesignContext`, but no registration or factory implementation was found. Workflow design JavaScript contributors depend on `IWorkflowDesignContext`. The original intent was an injectable design-time context, analogous to `IWorkflowExecutionContext`, so draft/activity values would not have to be passed through every method contract. It was not wired because the full design endpoint/UI suite is not defined yet. | Design-time JavaScript declaration contributors can fail at DI or fail when reading draft/activity context unless an external host supplies the context. The ambient-context assumption may still be useful, but it needs validation against the eventual design endpoint/UI model. | Plan a focused Workflows Design JavaScript/context unit: decide who creates design context, how it is scoped, whether endpoint/UI flows actually need an ambient DI context, and whether contributors should depend on a factory instead of a direct context. |
| `VariableExpressionDescriptor` throws for `HandlerFactory` and `Properties`. | Active code drift | `DefaultExpressionDescriptorProvider` always yields `VariableExpressionDescriptor`; `ExpressionEvaluator` calls `HandlerFactory` for the selected descriptor. | Evaluating a `Variable` expression can throw through an active service path. | Create a small implementation/test unit to supply a variable expression handler and descriptor properties, or remove the descriptor until variable expressions are supported. |
| `MultiDownloadableContentHandler` throws for `Priority`. | Unregistered placeholder / incomplete feature | The class implements `IDownloadableContentHandler`, but `HttpFeature` does not register it. If registered later, `Priority` throws. | Enumerable downloadable content is not handled by the feature today; future registration would introduce an executable failure path. | Decide whether enumerable downloadables are in scope. If yes, register the handler with a concrete priority and tests; if no, delete or quarantine the placeholder. |
| `ScriptExecutionContext` in `RunJavaScript/TestClasses` throws for some `IActivityExecutionContext` members. | Demo/dev endpoint fake context, not production runtime context | `Endpoint` creates this context inside a production source project; the current activity path does not call the throwing members, but the type is under a `TestClasses` namespace in `src`. | The endpoint can appear more production-ready than it is, and future activity behavior may hit unsupported members. | Quarantine the endpoint/context as dev/test-only or replace it with the future runtime execution context after the runtime seam is specified. |
| Test-project `NotImplementedException` members appear in local stub classes. | Test-only stubs | Throws are in test helper implementations for narrow interfaces. | Low; tests use only the implemented members. | No immediate action unless a test starts exercising the stubbed member. |
| `WorkflowDefinitionActivity.Execute` throws `NotSupportedException`. | Explicit construct-only deferral | Source comment and Unit 006 scope say workflow-as-activity execution is deferred. | Medium; consumers may mistake construction readiness for execution readiness. | Keep attached to the consumer/pinning/runtime execution unit. |

## Suggested Priority

1. Fix or explicitly defer `VariableExpressionDescriptor`, because it is on an active expression evaluation path.
2. Plan the Workflows Design JavaScript/context unit, because consumers depend on `IWorkflowDesignContext` but no local creation path is visible; preserve the ambient design-context idea as input, not as a settled decision.
3. Decide whether `MultiDownloadableContentHandler` should be implemented, registered, or removed.
4. Quarantine or rename the JavaScript activity endpoint fake context so it does not read as production runtime infrastructure.
5. Leave `WorkflowExecutionContext` and `WorkflowDefinitionActivity.Execute` attached to runtime execution planning.

## What This Report Does Not Do

- It does not implement the runtime execution seam.
- It does not claim behavioral coverage for the affected projects.
- It does not require immediate removal of every placeholder.
- It does not convert test-only stubs into production findings.

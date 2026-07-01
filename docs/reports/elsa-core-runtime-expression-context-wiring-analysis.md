# Elsa Core Runtime Expression-Context Wiring Analysis

Status: source-backed analysis for execution-layer planning. This is not a design decision, Speckit spec, or implementation plan.

> **Re-baselined 2026-07-02.** Parts of this analysis are overtaken by merged code — workflow variables/inputs ARE persisted, and generic JS accessors resolve at input-materialization time via the parameter-carrier this note recommends. See [runtime expression-context source reconciliation](runtime-expression-context-source-reconciliation.md) for the current baseline and the D1–D4 decision framing.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

Parent report: [Elsa Core runtime broken windows brainstorm](elsa-core-runtime-broken-windows-brainstorm.md), topic 8 (full-source review addition — this candidate was not maintainer-listed; it is source-derived from an elsa-foundation defect investigation).

Related evidence: [NotImplemented classification](notimplemented-classification.md) (`WorkflowExecutionContext` row), [Runtime execution pre-spec handoff](runtime-execution-pre-spec-handoff.md) ("Runtime JavaScript expression-context wiring shortcut").

## Inspection Scope

Elsa 3 source inspected from local checkout `/Users/sipke/Projects/Elsa/elsa-core`.

- Repository: `https://github.com/elsa-workflows/elsa-core.git`
- Branch: `release/3.8.0`
- Commit: `06580372` (short SHA at inspection time)
- Working tree note: the checkout had unrelated local changes in `src/apps/Elsa.ModularServer.Web/`. Files referenced below were inspected read-only.

This note is source-derived, not maintainer-listed: it originates from investigating why JavaScript workflow-input/variable/output accessors are dead code in `elsa-foundation` today, then comparing the underlying design against how `elsa-core` solves the same problem.

## Maintainer Concern

None stated directly. This is a source-derived candidate under brainstorm topic 8 ("full-source review additions"). It intersects maintainer-listed topic 5 ("Input evaluation memory register may be overcomplicated") in that both concern how expression evaluation reaches live workflow state, but this note is specifically about *context propagation mechanism*, not the memory-register design.

## Elsa-Foundation Current State (the defect)

`src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/` in elsa-foundation contributes JavaScript globals through a chain of `IScriptPreProcessor` implementations, invoked by `PreProcessScript` (`src/Elsa/Expressions/JavaScript/Handlers/PreProcessScript.cs`) in response to an `OnEvaluatingScript` event.

Five of the six pre-processors — plus one post-processor — take `IWorkflowExecutionContext` via constructor injection:

- `src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/WorkflowInputFunctionsPreProcessor.cs:10`
- `src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/WorkflowFunctionsPreProcessor.cs:9`
- `src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/VariableFunctionsPreProcessor.cs:12`
- `src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/ActivityOutputFunctionsPreProcessor.cs:10`
- `src/Elsa/Workflows/Runtime/JavaScript/PostProcessors/CopyVariablesToWorkflowContext.cs:13`

`IWorkflowExecutionContext` is never registered in DI anywhere under `src/` (no `AddScoped`/`AddSingleton`/`AddTransient`), and `WorkflowExecutionContext` (`src/Elsa/Workflows/Runtime/Core/WorkflowExecutionContext.cs`) is never constructed outside `tests/`. The real per-work-item execution loop, `WorkflowInvokeActivitySchedulerWorkHandler.cs:77`, creates a DI child scope via `IServiceScopeFactory.CreateAsyncScope()` but builds a `SimpleActivityExecutionContext` piecemeal from stores — it never constructs or registers a unified `IWorkflowExecutionContext` instance. Consequently, resolving any of the five pre-processors above throws, and they are dead code today.

The one sibling that avoids this, `MaterializationAccessorsPreProcessor` (same folder), takes nothing via constructor. It derives everything from the `IExpressionExecutionContext expressionContext` parameter already passed into `PreProcess(...)`, casting to `IMaterializationExpressionState` when available, and no-ops otherwise.

Non-JS expression evaluation (e.g. `LiquidExpressionHandler`, `src/Elsa/Expressions/Liquid/Services/LiquidExpressionHandler.cs:17`) never depends on `IWorkflowExecutionContext` at all — confirming the dependency is specific to, and unfinished in, the JS runtime layer.

## Elsa 3 (elsa-core) Comparable Design

`elsa-core` solves the equivalent problem — JS `getVariable`/`getInput`/`getOutputFrom`/workflow-identity accessors — with the same *shape* of pipeline (many independently registered handlers reacting to a "before evaluation" event), but the live execution context reaches those handlers by an explicit parameter, never by DI.

- `JintJavaScriptEvaluator.EvaluateAsync` (`src/modules/Elsa.Expressions.JavaScript/Services/JintJavaScriptEvaluator.cs:32`) takes `ExpressionExecutionContext context` as a plain method parameter (not a constructor-injected service) and publishes `EvaluatingJavaScript(engine, context, expression)` (`JintJavaScriptEvaluator.cs:40`) via `INotificationSender`.
- `ConfigureEngineWithCommonFunctions` (`src/modules/Elsa.Expressions.JavaScript/Handlers/ConfigureEngineWithCommonFunctions.cs:21`) is one of several `INotificationHandler<EvaluatingJavaScript>` handlers. It reads `notification.Context` (line 29) and registers closures directly against it — e.g. `engine.SetValue("getVariable", (Func<string, object?>)(name => context.GetVariableInScope(name)))` (line 45), `getInput` (line 46), `getOutputFrom` (line 47).
- `ConfigureEngineWithVariablesAndInputOutputAccessors` (`src/modules/Elsa.Expressions.JavaScript/Handlers/ConfigureEngineWithVariablesAndInputOutputAccessors.cs`) is the sibling handler responsible for named per-variable/per-input pascalized accessor functions — the direct analogue of elsa-foundation's `WorkflowInputFunctionsPreProcessor`/`WorkflowFunctionsPreProcessor`/`VariableFunctionsPreProcessor`.
- All handlers are discovered by assembly scan, `AddNotificationHandlersFrom<JavaScriptFeature>()` (`src/modules/Elsa.Expressions.JavaScript/Features/JavaScriptFeature.cs:91`) — DI registers the *handler classes* (stateless, container-managed), never the *per-evaluation execution state*.

The execution state itself lives in `ExpressionExecutionContext.TransientProperties`, a property bag keyed by well-known static object keys (`src/modules/Elsa.Workflows.Core/Extensions/ExpressionExecutionContextExtensions.cs:24` `WorkflowExecutionContextKey`, `:29` `ActivityExecutionContextKey`), populated once via `CreateActivityExecutionContextPropertiesFrom` (`ExpressionExecutionContextExtensions.cs:49`) when the real `ActivityExecutionContext` is constructed, and retrieved through typed extension methods: `GetWorkflowExecutionContext()` (`ExpressionExecutionContextExtensions.cs:78`), `TryGetWorkflowExecutionContext(...)` (`:73`), `GetActivityExecutionContext()` (`:88`), `TryGetActivityExecutionContext(...)` (`:98`).

Call chain, production code only: activity execution constructs `ActivityExecutionContext` → its properties dictionary is seeded with the live `WorkflowExecutionContext`/`ActivityExecutionContext` → an `ExpressionExecutionContext` carrying that dictionary is handed to `IExpressionEvaluator.EvaluateAsync(...)` → `JintJavaScriptEvaluator.EvaluateAsync(..., context, ...)` → `EvaluatingJavaScript(engine, context, ...)` published → every handler reads `notification.Context.GetWorkflowExecutionContext()` (or `TryGet...`) directly. The context can never be "missing" from a handler's perspective in the way elsa-foundation's constructor-injected pre-processors can silently fail to resolve — a handler either has a context parameter in hand, or the notification was never published.

## Current Elsa 4 (elsa-foundation) Architecture Constraints

- `Elsa.Workflows.Runtime.*` must not depend on `Elsa.Workflows.Design.*` at execution time (Elsa `§E2.2`, per [runtime-execution-pre-spec-handoff.md](runtime-execution-pre-spec-handoff.md)) — not directly implicated here, but any fix should be checked against it if `IExpressionExecutionContext` moves packages.
- No unified `IWorkflowExecutionContext`-shaped object is constructed anywhere in production; the runtime currently models per-activity state as `SimpleActivityExecutionContext` built from stores (`IActivityExecutionStateStore`, `IDurableValueStateStore`, etc.) inside `WorkflowInvokeActivitySchedulerWorkHandler`.
- `IExpressionExecutionContext` in elsa-foundation has no property-bag/transient-properties equivalent today — there is currently nowhere for a pre-processor to pull workflow-level state from *except* DI, which is very likely why the original five pre-processors were written with constructor injection despite the working precedent (`MaterializationAccessorsPreProcessor`) sitting right next to them.
- A parallel, already-documented instance of the same anti-pattern exists on the design side: `WorkflowDesignContext`/`IWorkflowDesignContext` (see [notimplemented-classification.md](notimplemented-classification.md)) — an "injectable ambient context" that was also never wired. Any resolution to this runtime-side question should consider whether it generalizes to the design-time case too.

## Compatibility Constraints

None blocking. The five affected pre-processors and the one post-processor are dead code today (DI cannot satisfy them), so there is no working production behavior to preserve or migrate — any fix is purely additive from a compatibility standpoint. No persisted state, external contract, or public API is implicated.

## Design Options Considered

1. **Register a `WorkflowExecutionContext` instance into the existing per-work-item DI scope.** Requires deciding, and then building, what a unified `IWorkflowExecutionContext` looks like against the current store-backed `SimpleActivityExecutionContext` model, then instantiating and registering it in `WorkflowInvokeActivitySchedulerWorkHandler` (and the sibling `WorkflowResumeBookmarkSchedulerWorkHandler` / `WorkflowParentActivityCompletionSchedulerWorkHandler`). Keeps the pre-processors' current constructor-injection shape. Risk: ties per-call ambient data to a DI scope lifetime, which must exactly match "one activity's expression evaluations" — any future concurrency or nested-evaluation scenario that shares a scope across more than one logical execution reintroduces the class of bug this analysis started from.
2. **Extend `IExpressionExecutionContext` with a transient-properties-style carrier (or a typed slot) plus extension methods**, mirroring elsa-core's `TransientProperties` + `GetWorkflowExecutionContext()`/`TryGetWorkflowExecutionContext(...)` pattern. Whoever constructs the real per-activity context populates the carrier once; pre-processors pull from the parameter already passed into `PreProcess(...)`, exactly like `MaterializationAccessorsPreProcessor` already does. Removes the DI dependency entirely — no scope-lifetime assumption to get wrong.
3. **Keep constructor injection, but change its lifetime model** (e.g. scoped-per-activity-execution rather than scoped-per-work-item) without adopting a parameter-carrier. Still couples ambient per-call data to container-managed lifetime; inherits the same class of risk as option 1, just with a narrower scope boundary.

## Preferred Direction

Option 2. It matches the upstream precedent (elsa-core never routes this data through DI), matches the one elsa-foundation pre-processor that already works correctly (`MaterializationAccessorsPreProcessor`), and removes an entire failure class (DI-scope-lifetime mismatch) rather than narrowing it. It does not, by itself, decide whether elsa-foundation needs a first-class `WorkflowExecutionContext` object per run (elsa-core's model) or a lighter read-only facade over `SimpleActivityExecutionContext`/store state — that question is separate and still open.

## Open Questions For The Architect

- Should elsa-foundation construct a first-class `WorkflowExecutionContext` per workflow run (elsa-core's model), or a lighter read-only facade over the existing store-backed `SimpleActivityExecutionContext` state, to populate the carrier?
- Should the same carrier mechanism resolve the parallel `WorkflowDesignContext`/`IWorkflowDesignContext` ambient-context question (see [notimplemented-classification.md](notimplemented-classification.md)), or are design-time and runtime-time contexts different enough to warrant separate answers?
- Does introducing a transient-properties-style bag on `IExpressionExecutionContext` require a constitution/glossary update (new shared contract shape), or is it purely an implementation detail of `Elsa.Expressions.Core`?

## Follow-Up Surface

Speckit work unit under [Runtime Execution Seam](../program-goals/runtime-execution-seam.md), gated on the incoming runtime architect being ready (per that bucket's active objectives). Do not wire DI registration or refactor the pre-processors as a drive-by fix ahead of that spec.

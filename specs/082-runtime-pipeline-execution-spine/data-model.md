# Phase 1 Data Model: Runtime Pipeline Execution Spine (Move 1)

Move 1 adds behavior/seams, not persisted data. The only data-shape change is the pipeline context refinement.

## Refined: pipeline contexts (`Models/RuntimePipelineContexts.cs`)

| Type | Field | Type | Notes |
|---|---|---|---|
| `WorkflowRuntimePipelineContext` | `WorkItem` | `RuntimeSchedulerWorkItem` | Always present; the originating dispatch. |
| | `WorkflowExecution` | `WorkflowExecutionState?` | Optional; null until `LoadState` populates it (Move 2) or when state does not yet exist (`Start`). |
| | `Scheduler` | `SchedulerState?` | Optional (unchanged intent). |
| `ActivityRuntimePipelineContext` | `WorkItem` | `RuntimeSchedulerWorkItem` | Always present. |
| | `WorkflowExecution` | `WorkflowExecutionState?` | Optional. |
| | `ActivityExecution` | `ActivityExecutionState?` | Optional; not derivable at dispatch without handler-internal payload parsing. |
| | `Scheduler` | `SchedulerState?` | Optional. |

Both remain `sealed record`s. `WorkflowExecutionId` is available via `WorkItem.WorkflowExecutionId`.

## New behavior contracts (no state)

- `IRuntimeWorkflowExecutionPipeline` / `IRuntimeActivityExecutionPipeline`: `ValueTask InvokeAsync(context, terminalDelegate)` + `RuntimePipelinePlan Plan { get; }`.
- `IRuntimeSchedulerPipelineSelector`: `RuntimePipelineKind Select(RuntimeSchedulerWorkItem workItem)`.
- `IRuntimeExecutionPipelineDispatcher`: `ValueTask DispatchAsync(RuntimeSchedulerWorkItem workItem, IWorkflowSchedulerWorkHandler handler, CancellationToken ct)`.

## Selection mapping (authoritative)

| `WorkflowExecutionCommandKind` | Pipeline | Discriminator |
|---|---|---|
| `Start`, `Checkpoint`, `Cancel` | Workflow | command kind |
| `CompleteActivity` (CompletionKind ≠ ParentCompletionEvaluation, or no/invalid payload) | Workflow | kind + payload |
| `CompleteActivity` (CompletionKind = ParentCompletionEvaluation) | Activity | kind + payload |
| `ScheduleActivity`, `StartActivity`, `InvokeActivity`, `ResumeBookmark`, `CreateBookmark` | Activity | command kind |
| all others reaching the drainer (`RunSchedulerWork`, `ContinueVolatileWait`, `Pause`/`Unpause`, `DeliverSignal`, `GeneratedEvent`) | Workflow (default) | command kind |

## Module contribution DX (folded into Move 1)

New types (all `Elsa.Workflows.Runtime.Core.Middleware`, Runtime-owned):

- `RuntimeMiddlewareAttribute(string slot) { int Order; string? Name }` — default placement declared on a middleware type.
- `WorkflowRuntimeMiddlewareContribution(Type MiddlewareType, string Slot, int Order, string? Name)` and `ActivityRuntimeMiddlewareContribution(...)` — DI-collected placement requests.
- `RuntimeMiddlewareServiceCollectionExtensions.AddWorkflow/ActivityRuntimeMiddleware<T>(slot?, order?, name?)` — atomic register-type + record-contribution; placement = explicit args ?? attribute ?? (throw for missing slot).

Builder additions (`RuntimePipelinePlanBuilder` + the two concrete builders):

- `Use(Type, slot, order = 0, name = null)` — non-generic placement (used to apply contributions); validates the type implements the pipeline's middleware interface.
- `Replace<TOld, TNew>()` / `Remove<T>()` — swap/drop a registration (including built-ins) at its placement; throw if the target is absent.

Ordering rule change in `BuildPlan()`:

- Sort key changed from `(SortOrder, Order, RegistrationIndex)` to `(SortOrder, Order, MiddlewareType.FullName)` — deterministic, load-order-independent.
- Two **distinct** middleware sharing the same `(slot, order)` → `InvalidOperationException` naming the conflict; built-ins occupy order 0, so a module at order 0 in a built-in slot collides and is told to pick a negative (before) or positive (after) order.

Feature wiring: the `IRuntimeWorkflow/ActivityExecutionPipeline` factory applies the DI-collected contributions to a fresh builder, builds the plan, logs the resolved plan at Debug, and constructs the pipeline. Built-ins and module contributions flow through the same builder (no privileged path).

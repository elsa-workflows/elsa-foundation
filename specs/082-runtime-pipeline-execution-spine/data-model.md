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

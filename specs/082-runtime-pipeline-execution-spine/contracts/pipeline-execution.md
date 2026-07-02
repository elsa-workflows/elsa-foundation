# Contract: Runtime Pipeline Execution Spine (Move 1)

## `IRuntimeWorkflowExecutionPipeline` / `IRuntimeActivityExecutionPipeline`

```csharp
public interface IRuntimeWorkflowExecutionPipeline
{
    RuntimePipelinePlan Plan { get; }
    ValueTask InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate terminal);
}

public interface IRuntimeActivityExecutionPipeline
{
    RuntimePipelinePlan Plan { get; }
    ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate terminal);
}
```

**Guarantees**
- Middleware run in `Plan.Steps` order; each `next` invokes the following middleware, the innermost `next` is `terminal`.
- With only pass-through placeholders, `InvokeAsync(context, terminal)` is observationally equal to `terminal(context)` (behavior preservation).
- A middleware that does not call `next` prevents `terminal` (the handler) from running.
- Exceptions thrown by middleware or `terminal` propagate to the caller unchanged.

## `IRuntimeSchedulerPipelineSelector`

```csharp
public interface IRuntimeSchedulerPipelineSelector
{
    RuntimePipelineKind Select(RuntimeSchedulerWorkItem workItem);
}
```

**Guarantees**
- Total function over every `WorkflowExecutionCommandKind`; never throws (malformed `CompleteActivity` payload → Workflow).
- Mapping per [data-model.md](../data-model.md) selection table; `CompleteActivity` disambiguated by payload `CompletionKind`.

## `IRuntimeExecutionPipelineDispatcher`

```csharp
public interface IRuntimeExecutionPipelineDispatcher
{
    ValueTask DispatchAsync(
        RuntimeSchedulerWorkItem workItem,
        IWorkflowSchedulerWorkHandler handler,
        CancellationToken cancellationToken = default);
}
```

**Guarantees**
- Selects the pipeline, builds the matching context from `workItem`, and invokes the pipeline with `terminal = _ => handler.HandleAsync(workItem, cancellationToken)`.
- Invoked at exactly one call site (`WorkflowSchedulerDrainer.DispatchAsync`) and only when wired; absence ⇒ direct `handler.HandleAsync`.

## Drainer seam (`WorkflowSchedulerDrainer`)

- Gains one optional `IRuntimeExecutionPipelineDispatcher?` constructor parameter (defaulted null).
- Dispatch line becomes: dispatcher present ⇒ `dispatcher.DispatchAsync(workItem, handler, ct)`; else ⇒ `handler.HandleAsync(workItem, ct)`.
- All other drainer behavior (fault capture, result records, terminal-status re-check) unchanged.

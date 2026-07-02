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

## Module contribution surface

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class RuntimeMiddlewareAttribute(string slot) : Attribute
{
    public string Slot { get; }
    public int Order { get; init; }
    public string? Name { get; init; }
}

// One atomic call: registers the type in DI AND records its placement.
IServiceCollection AddWorkflowRuntimeMiddleware<TMiddleware>(this IServiceCollection, string? slot = null, int? order = null, string? name = null)
    where TMiddleware : class, IWorkflowRuntimeMiddleware;
IServiceCollection AddActivityRuntimeMiddleware<TMiddleware>(this IServiceCollection, string? slot = null, int? order = null, string? name = null)
    where TMiddleware : class, IActivityRuntimeMiddleware;

// Builder ops (both concrete builders):
Use(Type middlewareType, string slot, int order = 0, string? name = null); // non-generic, validates the middleware interface
Builder Replace<TOld, TNew>();
Builder Remove<TMiddleware>();
```

**Guarantees**
- Placement resolves to explicit args, else the `[RuntimeMiddleware]` attribute, else a thrown error (missing slot).
- Built-ins, first-party, and third-party middleware use the same path; built-ins are marked `IsBuiltIn` and sit at order 0.
- `BuildPlan()` orders by `(slot sort-order, order, built-ins-first, type full-name)` — deterministic, load-order-independent. A module at the built-in's order 0 runs after it; a negative order runs before. It throws `InvalidOperationException` on two distinct **module** middleware sharing a `(slot, order)`, and collapses an identical repeated registration to one (idempotent).
- `Replace` throws if the target is absent; `Remove` is idempotent (no-op when absent).
- The feature applies DI-collected contributions to the builder before composing, and logs the resolved plan at Debug on first composition. Concrete-neighbour ordering is not supported.

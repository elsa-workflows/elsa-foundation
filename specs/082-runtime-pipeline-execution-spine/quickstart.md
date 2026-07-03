# Quickstart: Register a runtime execution middleware (after Move 1)

Move 1 makes the runtime pipeline live. A module can now insert a cross-cutting concern into runtime execution by registering middleware against a named slot.

```csharp
// 1. Implement middleware over the desired pipeline context.
public sealed class TracingActivityMiddleware : ActivityRuntimeMiddlewareBase
{
    public override async ValueTask InvokeAsync(
        ActivityRuntimePipelineContext context,
        ActivityRuntimeMiddlewareDelegate next)
    {
        // context.WorkItem is always available; typed state is populated by the LoadState slot in Move 2.
        using var _ = Trace($"activity-work:{context.WorkItem.CommandKind}:{context.WorkItem.WorkflowExecutionId}");
        await next(context); // runs the rest of the pipeline, ending in the scheduler work handler
    }
}

// 2. Register it against a slot when building the activity pipeline.
var plan = new ActivityRuntimePipelineBuilder()
    .Use<TracingActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: -10)
    .BuildPlan();
```

At runtime, when the scheduler drains an activity-kind work item, `WorkflowSchedulerDrainer` dispatches it through `IRuntimeExecutionPipelineDispatcher`, which selects the activity pipeline and invokes it around `handler.HandleAsync(...)`. The middleware runs; the handler is the inner terminal delegate.

## Verifying it actually runs (the guardrail)

`RuntimeExecutionPipelineDispatchTests` registers a marker middleware, dispatches a real work item through the drainer, and asserts the marker ran **and** the handler ran — the ADR-required guardrail against silent re-orphaning.

## Behavior preservation

With only the built-in placeholder middleware registered, `InvokeAsync` reduces to a direct call of the handler. Existing workflows, state, and checkpoints are unchanged.

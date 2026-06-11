# Contract: Runtime Pause Boundary Enforcement

## `IWorkflowSchedulerPauseGate`

```csharp
ValueTask<SchedulerPauseDecision?> EvaluateAsync(
    RuntimeSchedulerWorkItem workItem,
    CancellationToken cancellationToken = default);
```

Returns `null` when a scheduler work item has no pause boundary in this slice. Returns a `SchedulerPauseDecision` when the item maps to a safe runtime pause boundary.

## Default Boundary Mapping

- `StartActivity` -> `BeforeActivityExecutionStart`
- `InvokeActivity` -> `BeforeActivityExecutionStart`
- `GeneratedEvent` -> `BeforeGeneratorEmission`

## Drainer Behavior

- The drainer peeks at the next queued work item before dequeue.
- If the pause gate returns a blocked decision, the drainer emits one pause-blocked result and stops without dequeueing the item.
- If the pause gate returns `null` or an allowed decision, the drainer dequeues and dispatches the work item normally.

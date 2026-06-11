# Contract: Runtime Generator Emission Scheduler

## `IRuntimeGeneratorEmissionScheduler`

```csharp
ValueTask<RuntimeGeneratorEmissionScheduleResult> ScheduleAsync(
    RuntimeGeneratorEmissionScheduleRequest request,
    CancellationToken cancellationToken = default);
```

Schedules one in-workflow `GeneratedEvent` as ordered runtime scheduler work.

## Request

- `GeneratedEvent`: Required generator emission contract.
- `Reason`: Required scheduler reason.
- `EnqueuedAt`: Optional runtime enqueue timestamp. Defaults to current runtime time.
- `Metadata`: Optional metadata copied to the generated-event scheduler work item.

## Result

- `GeneratedEventWorkItem`: The deterministic generated-event scheduler work payload.
- `SchedulerWorkItem`: The queued runtime scheduler work item returned by `IWorkflowSchedulerWorkQueue`.

## Required Behavior

- Work item ID, command ID, envelope ID, and idempotency key are deterministic from workflow execution ID and generated event ID.
- The runtime scheduler work command kind is `WorkflowExecutionCommandKind.GeneratedEvent`.
- The generated-event payload remains scheduler data and does not create an activity execution.
- The default implementation does not mutate durable continuation state directly.

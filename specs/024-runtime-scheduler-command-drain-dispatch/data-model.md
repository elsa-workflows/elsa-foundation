# Data Model: Runtime Scheduler Command Drain Dispatch

## IWorkflowSchedulerDrainPolicy

Decides whether a recorded scheduler work item should trigger a drain.

Method:

- `CreateDrainRequest(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerWorkItem workItem)`

Rules:

- Returning a `RuntimeSchedulerDrainRequest` triggers a drain.
- Returning `null` records scheduler work without draining.
- The default policy drains immediately for the work item's `WorkflowExecutionId`.

## IWorkflowSchedulerDrainObserver

Observes drain results produced by command processing.

Method:

- `OnDrainedAsync(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerDrainResult result, CancellationToken cancellationToken = default)`

Rules:

- Observers are contributor extensions.
- Observers are projections/notifications, not continuation state.
- Observer failures are command-processing failures because they run inside the mailbox command path.

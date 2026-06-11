# Data Model: Runtime Scheduler Drain Contract

## RuntimeSchedulerDrainRequest

Fields:

- `WorkflowExecutionId`
- `MaxWorkItems`

Rules:

- `WorkflowExecutionId` is required.
- `MaxWorkItems`, when provided, must be positive.

## RuntimeSchedulerDrainResult

Fields:

- `WorkflowExecutionId`
- `StartedAt`
- `CompletedAt`
- `DrainedCount`
- `StoppedOnFault`
- `Items`

Rules:

- `DrainedCount` equals the number of item results.
- `StoppedOnFault` is true when any item result has status `Faulted`.

## RuntimeSchedulerWorkItemResult

Fields:

- `WorkItemId`
- `WorkflowExecutionId`
- `CommandKind`
- `Status`
- `HandlerName`
- `StartedAt`
- `CompletedAt`
- `Error`

Rules:

- Error text is required for faulted results and forbidden for completed results.

## IWorkflowSchedulerDrainer

Drains queued scheduler work for one workflow execution.

Method:

- `DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)`

## IWorkflowSchedulerWorkHandler

Handles one drained scheduler work item.

Methods:

- `CanHandle(RuntimeSchedulerWorkItem workItem)`
- `HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)`

Rules:

- The first ordinary handler that returns `true` handles the item.
- Fallback handlers implement `IFallbackWorkflowSchedulerWorkHandler` and run only after ordinary handlers decline the item.
- The default no-op fallback handler handles all command kinds.
- Handler dispatch is not activity execution.

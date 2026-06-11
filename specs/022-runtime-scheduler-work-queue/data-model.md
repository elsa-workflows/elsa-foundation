# Data Model: Runtime Scheduler Work Queue

## RuntimeSchedulerWorkItem

Command-shaped scheduler work recorded after an execution agent accepts a `WorkflowExecutionCommandEnvelope`.

Fields:

- `WorkItemId`
- `WorkflowExecutionId`
- `CommandId`
- `CommandKind`
- `EnvelopeId`
- `IdempotencyKey`
- `EnqueuedAt`
- `RecordedAt`
- `Sequence`
- `Payload`
- `CommandMetadata`
- `EnvelopeMetadata`

Rules:

- `WorkItemId`, workflow execution ID, command ID, envelope ID, and idempotency key are required.
- `WorkflowExecutionId` must match the command workflow execution ID before the envelope can exist.
- Payload is cloned when recorded so callers can dispose their source JSON document.

## RuntimeSchedulerWorkQuery

Read query for scheduler work.

Fields:

- `WorkflowExecutionId`
- `Limit`

Rules:

- `WorkflowExecutionId` is required.
- `Limit`, when provided, must be positive.

## IWorkflowSchedulerWorkQueue

Provider-neutral queue boundary for scheduler work.

Methods:

- `EnqueueAsync`
- `ListAsync`
- `DequeueAsync`

Rules:

- Queue ordering is per workflow execution.
- Duplicate work item IDs return the original queued item within the same workflow execution.
- Dequeue removes one item from the workflow execution queue.

## WorkflowSchedulerCommandProcessor

Default `IWorkflowExecutionCommandProcessor` that records accepted command envelopes as scheduler work.

Rules:

- Runs inside the execution agent mailbox.
- Does not execute scheduler work.
- Does not checkpoint.
- Does not load Design-owned workflow documents.

# Data Model: Runtime In-Process Execution Agent Provider

## InProcessWorkflowExecutionAgentProvider

Default single-node provider for actor-like workflow execution agents.

Fields:

- `Capabilities`
- Active agent registry keyed by `WorkflowExecutionId`
- Lifecycle gate that serializes activation and passivation
- Activation counter for inspectable agent IDs
- Command processor

## InProcessWorkflowExecutionAgent

In-memory mailbox for one workflow execution ID.

Fields:

- `WorkflowExecutionId`
- `AgentId`
- `ActivatedAt`
- `Status`
- Processed idempotency keys
- Mailbox semaphore

Rules:

- One agent serializes command processing with a single mailbox semaphore.
- Replacement activation waits until passivation has marked the old mailbox unavailable.
- A duplicate idempotency key returns `Duplicate` and does not invoke the processor.
- A passivated agent returns `Deferred` for later enqueue attempts.
- A workflow ID mismatch returns `Rejected`.

## IWorkflowExecutionCommandProcessor

Runtime command processing seam invoked by in-process agents after dispatch metadata has been accepted.

This is not scheduler behavior. It exists so the provider can own mailbox ordering while later runtime slices decide what each command does.

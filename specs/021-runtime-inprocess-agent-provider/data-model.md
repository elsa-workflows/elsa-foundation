# Data Model: Runtime In-Process Execution Agent Provider

## InProcessWorkflowExecutionAgentProvider

Default single-node provider for actor-like workflow execution agents.

Fields:

- `Capabilities`
- Active agent registry keyed by `WorkflowExecutionId`
- Per-workflow lifecycle gates that serialize activation and passivation for each `WorkflowExecutionId`
- Activation counter for inspectable agent IDs
- Command processor
- Maximum processed idempotency-key count per agent

## InProcessWorkflowExecutionAgent

In-memory mailbox for one workflow execution ID.

Fields:

- `WorkflowExecutionId`
- `AgentId`
- `ActivatedAt`
- `Status`
- Bounded processed idempotency keys
- Processed idempotency-key insertion order
- Mailbox semaphore

Rules:

- One agent serializes command processing with a single mailbox semaphore.
- Replacement activation waits until passivation has marked the old mailbox unavailable.
- Passivating one workflow execution does not block activation or passivation for unrelated workflow execution IDs.
- A duplicate idempotency key returns `Duplicate` and does not invoke the processor.
- Processed idempotency keys are retained up to the provider-configured per-agent limit.
- A passivated agent returns `Deferred` for later enqueue attempts.
- A workflow ID mismatch returns `Rejected`.

## IWorkflowExecutionCommandProcessor

Runtime command processing seam invoked by in-process agents after dispatch metadata has been accepted.

This is not scheduler behavior. It exists so the provider can own mailbox ordering while later runtime slices decide what each command does.

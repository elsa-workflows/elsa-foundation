# Data Model: Runtime API Agent Dispatch

## `WorkflowExecutionStartDispatchRequest`

Runtime request to start one workflow execution from a runtime-owned executable artifact.

- `ArtifactId`: executable artifact ID requested by the caller.
- `WorkflowExecutionId`: optional caller-provided workflow execution ID; generated when absent.
- `IdempotencyKey`: optional command idempotency key; generated from the workflow execution and artifact when absent.
- `RequestedBy`: logical runtime caller name for agent activation metadata.
- `Metadata`: command/envelope metadata copied into the runtime command path.

## `WorkflowExecutionStartCommandPayload`

Payload stored on the `Start` command.

- `PinnedExecutable`: exact `WorkflowExecutableIdentity` loaded from the executable store.
- `RequestedArtifactId`: artifact ID supplied by the caller.

## `WorkflowExecutionStartDispatchResult`

Result returned to Runtime API after agent dispatch.

- `WorkflowExecutionId`: workflow execution ID used for activation and command dispatch.
- `PinnedExecutable`: exact executable identity in the command payload.
- `CommandDispatch`: mailbox dispatch result from the execution agent.
- `Agent`: descriptor for the selected execution agent.

## `IRuntimeExecutionIdGenerator`

Provider-neutral runtime ID generator for this seam.

- Generates workflow execution IDs.
- Generates workflow execution command IDs.
- Generates workflow execution command envelope IDs.

# Data Model: Runtime Execution Agent Provider Contract

## WorkflowExecutionCommandEnvelope

Provider-neutral command delivery envelope.

Fields:

- `EnvelopeId`
- `Command`
- `WorkflowExecutionId`
- `IdempotencyKey`
- `Sequence`
- `DeliveryMode`
- `EnqueuedAt`
- `Metadata`

Validation:

- `WorkflowExecutionId` must match `Command.WorkflowExecutionId`.
- `IdempotencyKey` is required for provider retry/deduplication.
- `Sequence` is optional, but cannot be negative when supplied.

## WorkflowExecutionAgentActivationRequest

Provider-neutral request to activate or resolve the mailbox for one workflow execution.

Fields:

- `WorkflowExecutionId`
- `ActivationReason`
- `RequestedAt`
- `RequestedBy`
- `RequiredCapabilities`
- `Metadata`

## WorkflowExecutionAgentDescriptor

Inspectable runtime provider descriptor for one active/resolved agent.

Fields:

- `WorkflowExecutionId`
- `AgentId`
- `ProviderName`
- `Status`
- `Capabilities`
- `ActivatedAt`
- `LastCheckpointId`
- `Metadata`

## WorkflowExecutionAgentPassivationRequest

Provider-neutral request to passivate/deactivate an agent at a safe boundary.

Fields:

- `WorkflowExecutionId`
- `Boundary`
- `RequestedAt`
- `Reason`
- `Metadata`

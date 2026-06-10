# Runtime Contract Surface

## Artifact Contract

`WorkflowExecutable` is the runtime-owned artifact consumed by execution. Runtime executions pin `WorkflowExecutableIdentity`.

Required identity fields:

- `ArtifactId`
- `DefinitionId`
- `DefinitionVersionId`
- `ArtifactVersion`
- `ArtifactHash`

## State Contracts

The first slice defines these separate continuation-state contracts:

- `WorkflowExecutionState`
- `SchedulerState`
- `ActivityExecutionState`
- `DurableValueState`

Bookmarks, incidents, operational state, history, and audit projections are deferred to later slices.

## Checkpoint Contracts

Checkpoint names are constants in `RuntimeCheckpointNames`. Policy hooks:

- `IRuntimeCheckpointPersistencePolicy`
- `IRuntimeCheckpointWriter`

The default policy is immediate flush. Storage providers may replace it later without changing checkpoint names.

## Agent Contracts

`IWorkflowExecutionAgentProvider` resolves an `IWorkflowExecutionAgent` by workflow execution id. The provider is responsible for enforcing one active mailbox/agent per workflow execution id.

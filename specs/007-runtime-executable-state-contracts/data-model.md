# Data Model: Runtime Executable Artifact And Execution State Contracts

## WorkflowExecutable

Runtime-owned executable artifact produced by compile/publish. It owns artifact identity, executable nodes, resume target placeholders, creation/publication timestamps, and compatibility metadata.

Validation:

- `Identity.ArtifactId`, `ArtifactVersion`, and `ArtifactHash` are required.
- `DefinitionId` and `DefinitionVersionId` are source references only; Runtime does not load Design state from them during execution.
- Executable nodes are keyed by `ExecutableNodeId`.

## ExecutableNode

Runtime-owned node inside a workflow executable.

Validation:

- `ExecutableNodeId` is the runtime execution key.
- `AuthoredActivityId` is trace/source linkage only.
- `ActivityType` and `ActivityTypeVersion` identify the required runtime activity support.

## WorkflowExecutionState

Continuation state for one workflow execution pinned to an exact executable artifact.

Validation:

- `WorkflowExecutionId` is required.
- `PinnedExecutable` is required and must not be replaced implicitly by a newer artifact.
- Runtime status and timestamps live here; scheduler queues, activity executions, bookmarks, durable values, incidents, and operational markers live in separate state contracts.

## ActivityExecution / ActivityExecutionState

`ActivityExecution` is the durable identity for one concrete execution of one executable node. `ActivityExecutionState` owns lifecycle and relationship state for that execution.

Validation:

- Multiple activity executions may reference the same `ExecutableNodeId` and `AuthoredActivityId`.
- Raw outputs and evaluated inputs are not durable activity execution state.

## SchedulerState

Minimal scheduler continuation state.

Validation:

- Pending work references executable nodes and activity executions.
- Volatile waits are scoped to workflow execution, activity execution, and branch.
- Scheduler state is the single-writer queue/continuation surface; this unit does not implement scheduling behavior.

## DurableValueState

Declared durable runtime value state.

Validation:

- `Lifecycle = None` must use `Storage = None` and cannot carry inline or external durable state.
- Inline values are JSON-first.
- External values store a reference/locator, not an arbitrary CLR object.
- Activity output persistence happens only through capture into a declared durable value.

## RuntimeCheckpoint

Named runtime boundary for state changes and post-commit intents.

Validation:

- Checkpoint name describes what changed.
- Persistence policy decides when/how to flush independently from checkpoint semantics.

## WorkflowExecutionAgent

Provider-owned mailbox abstraction for one workflow execution id.

Validation:

- Commands target one `WorkflowExecutionId`.
- Actor frameworks remain provider implementations; checkpoint state remains the source of truth.

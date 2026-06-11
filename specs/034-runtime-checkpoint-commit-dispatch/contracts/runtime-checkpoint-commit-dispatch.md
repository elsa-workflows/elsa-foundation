# Contract: Runtime Checkpoint Commit Dispatch

## Checkpoint Handler

`WorkflowCheckpointSchedulerWorkHandler` handles `WorkflowExecutionCommandKind.Checkpoint`.

1. Deserialize `RuntimeCheckpointCommandPayload`.
2. Resolve every referenced `ActivityExecutionState` from `IActivityExecutionStateStore`.
3. Build `RuntimeCheckpointCommit` with:
   - `RuntimeCheckpoint.Name` from payload `CheckpointName`.
   - `RuntimeCheckpoint.ActivityExecutionIds` from the payload.
   - `RuntimeCheckpointStateChangeSet.ActivityExecutions` from resolved activity states.
   - Empty unsupported state lanes for this slice.
4. Dispatch the envelope through `RuntimeCheckpointCommitter`.

Missing referenced activity state faults the scheduler work before the committer is called.

## Defaults

The current runtime composition contributes:

- `ImmediateRuntimeCheckpointPersistencePolicy`
- `InMemoryRuntimeCheckpointWriter`
- `NoopRuntimePostCommitIntentDispatcher`
- `RuntimeCheckpointCommitter`

These are in-process defaults for the current runtime seam, not durable provider implementations.

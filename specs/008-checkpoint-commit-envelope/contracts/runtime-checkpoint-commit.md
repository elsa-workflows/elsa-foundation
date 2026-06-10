# Runtime Checkpoint Commit Contract

Runtime checkpoint commits are provider-facing contracts in `Elsa.Workflows.Runtime.Core`.

## Commit Flow

1. Runtime code creates `RuntimeCheckpoint`.
2. Runtime code packages state changes and post-commit intents into `RuntimeCheckpointCommit`.
3. `IRuntimeCheckpointPersistencePolicy.DecideAsync` chooses `Immediate`, `Deferred`, or `Skip`.
4. `IRuntimeCheckpointWriter.WriteAsync` receives the full commit envelope and decision for `Immediate` and `Deferred`.
5. `IRuntimePostCommitIntentDispatcher.DispatchAsync` receives intents only after the writer completes successfully.

## Extension Points

- `IRuntimeCheckpointPersistencePolicy`: decides persistence timing without changing checkpoint semantics.
- `IRuntimeCheckpointWriter`: persists or records the full checkpoint commit envelope.
- `IRuntimePostCommitIntentDispatcher`: dispatches committed intents after checkpoint persistence succeeds.

## Non-Goals

- No concrete checkpoint store.
- No durable bookmark index.
- No outbox processor.
- No actor provider implementation.

# Contract: Runtime Post-Commit Outbox Recording

`RuntimeCheckpointCommitter` records committed post-commit intents into `IRuntimePostCommitOutboxStore` before immediate dispatch.

## Recording Rule

When a checkpoint commit is persisted:

1. `IRuntimeCheckpointWriter.WriteAsync` succeeds.
2. Every `RuntimePostCommitIntent` in the commit is saved as a pending `RuntimePostCommitOutboxItem`.
3. Immediate dispatch starts only after all pending records are saved.
4. Each successful dispatch records `RuntimePostCommitOutboxStatus.Delivered`.
5. A failed dispatch records `RuntimePostCommitOutboxStatus.FailedFinal` for the failed item, then preserves existing dispatch exception behavior.

## Item Identity

The default outbox item ID is deterministic:

```text
{CommitId}:{IntentId}
```

The pending item uses the intent's recorded time and the checkpoint occurrence time as the initial availability boundary.

## Non-Scope

- Processor loops.
- Ownership/claiming.
- Retry policy selection.
- Background redelivery.

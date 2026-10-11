# Contract: continuations written in the checkpoint commit (FR-001–FR-003)

Audience: runtime checkpoint commit store providers (`IRuntimeCheckpointCommitStore`) and the committer
(`RuntimeCheckpointCommitter`). This extends [ADR 0020](../../../docs/adr/0020-runtime-checkpoint-commit-post-commit-work.md)
as clarified on 2026-10-11; it does not replace the outbox contract.

## Capability

```csharp
// Runtime/Core/Contracts/IRuntimeCheckpointCommitStore.cs
bool CanStageSchedulerWork(IWorkflowSchedulerWorkQueue schedulerWorkQueue) => false;
```

- C1. A store returns `true` only if a scheduler work item it writes inside its commit transaction is visible to
  `schedulerWorkQueue` (same rows, same identity scope) as soon as, and only if, that transaction commits.
- C2. The answer may depend on the scope (both arguments are scoped services). The committer asks on every commit.
- C3. A decorator forwards the question to the store and queue it wraps only when it would pass the commit straight
  through. `CoalescingRuntimeCheckpointCommitStore` answers `false` while a coalescing session applies to the execution.
- C4. Third-party stores inherit `false` and keep the outbox path with no code change.

## Committer obligations

- C5. The committer marks an outbox entry in `SchedulerWorkInCommit` only when all hold: `RuntimeHopCollapseOptions.Enabled`;
  no coalescing session applies to the commit's execution; `CanStageSchedulerWork(queue)` is `true` for the queue
  resolved in the committer's own scope; the entry's intent kind is `EnqueueSchedulerWork`.
- C6. Every other intent kind, and every entry on a composition failing C5, is recorded in the outbox exactly as today.
- C7. `PostCommitOutbox` keeps one pending entry per intent whether or not the entry is marked, so the commit's
  fingerprint does not depend on the transport.
- C8. After the store returns, the committer requires `PendingPostCommitWorkIds ∪ ScheduledSchedulerWorkItemIds` to equal
  the folded outbox ids, with the two sets disjoint, and fails the commit result otherwise.
- C9. `PublishInProcessHopWorkItems` (spec 109) publishes only intents that went to the outbox.

## Store obligations (a store answering `true`)

- C10. For each marked id, write the scheduler work item carried by the intent (its materialized item when present, else
  its payload, which MUST be equal) inside the same transaction as the checkpoint marker, and write no outbox row for it.
- C11. Identity, payload and order are the item's own: the row is the one `EnqueueAsync` would write for that item.
- C12. An item already present under the same `WorkItemId` (same scope and execution) is kept as-is and nothing is
  written or overwritten, whatever the staged item's content, exactly as `EnqueueAsync` keeps and returns it today
  (create-only by identity, spec edge case as corrected 2026-10-11). There is no conflict outcome. A damaged existing row
  fails the commit, as `ReadChecked` does for enqueue.
- C13. The marker records, as `PendingPostCommitWorkIds`, only the outbox rows it wrote.
- C14. Replay: when the marker exists and the fingerprint matches, report `ScheduledSchedulerWorkItemIds` as the commit's
  `EnqueueSchedulerWork` outbox ids that the marker does not list as pending. Write nothing.
- C15. A rollback (any failure before the transaction commits) leaves neither the checkpoint, the marker, the outbox rows
  nor the scheduler rows (US1 AS2). The existing marker reconciliation after an ambiguous commit is unchanged.
- C16. A store answering `false` refuses a commit whose `SchedulerWorkInCommit` is not empty
  (`NotSupportedException`), so a misrouted commit can never drop a continuation.

## Contract tests

Added to the shared provider contract `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/RuntimeCheckpointCommitContract.cs`
and run by the SQLite tests and the PostgreSQL/SQL Server/MySQL provider smoke tests
(`.../ProviderTests/EfRuntimeCheckpointSchedulerWorkParticipantProviderSmokeTests.cs`):

| Case | Covers |
|---|---|
| marked continuation exists as a queued row after commit, no outbox row, marker pending set excludes it | C10, C13, US1 AS1 |
| two marked continuations keep the order the outbox path gives (`ListAsync` order equals `WorkOrderKey` order) | C11, edge case "several continuations" |
| transaction failure after staging leaves no checkpoint, marker, outbox or scheduler row | C15, US1 AS2 |
| item already present under the same `WorkItemId` (identical, and with different content): commit succeeds, existing row unchanged | C12, edge case "redelivery" |
| replay of a committed commit reports the scheduled ids and writes nothing | C14 |
| replay after the switch flips (same commit id, entry no longer marked) resolves without a replay conflict and delivers nothing twice | C7, C14 |
| a marked commit sent to the in-memory store throws `NotSupportedException` | C16 |

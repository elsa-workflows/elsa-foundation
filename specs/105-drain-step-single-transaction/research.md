# Research

## Existing seams reused (no reinvention)

- **Checkpoint commit is already multi-store atomic.** `GroundworkRuntimeCheckpointWriter.ApplyAtomicallyAsync` opens one commit-ledger unit-of-work (`TransactionBoundary.CrossUnitAtomic`) that already includes `SchedulerWorkItemDocumentKind` in `RuntimeCheckpointCommitScope`, and already deletes scheduler work items inside it for `ActivityScopeCleanupRequest` (`ApplyActivityScopeCleanupsAsync` → `SchedulerWorkQueue.DeleteAsync`). The consume-change reuses that same in-UoW delete pattern, adding a fence check.
- **Post-commit outbox folding is the template.** `RuntimePostCommitOutboxItems.CreatePendingChanges` + `RuntimeCheckpointStateChangeSet.WithPostCommitOutbox` + the committer's post-commit count guard are mirrored one-to-one by the consume-change plumbing.
- **Fence-checked delete already exists.** `GroundworkWorkflowSchedulerWorkQueue.CompleteClaimAsync` fence-checks `revision + token + owner` then deletes by expected version. The consume path narrows the fence to `token + owner` (renewal-stable) and deletes by the current version loaded in the UoW.
- **Ownership fence transport is the shape to mirror, but scoped not AsyncLocal.** `RuntimeCheckpointCommitter.AttachExpectedFence` reads `IRuntimeExecutionOwnershipContextAccessor.Current` (a per-drain AsyncLocal push/pop). The claim is per-dispatch and short-lived; it flows through a scoped accessor shared by the same-scope drainer and committer (RT-7 forbids a new AsyncLocal service locator).

## Renewal race analysis

The renewal loop mutates the claimed document's provider revision concurrently with the handler. Folding the ack on `revision` would race (a renewal mid-handler would invalidate a revision-based fence). `RenewClaimAsync` keeps `ClaimOwnerId`/`ClaimToken` constant, so fencing on `owner + token` is race-free while still catching a successor reclaim (which advances the token via `ClaimAsync`). Confirmed in both `InMemoryWorkflowSchedulerWorkQueue` and `GroundworkWorkflowSchedulerWorkQueue`.

## Coalesced-mode analysis

In coalesced mode the durable scheduler queue is advanced once per segment by `RuntimeCoalescingSession.AdvanceInnerQueueAsync` after the folded flush lands, decoupled from each checkpoint. A durable consume-delete folded into the flush would double-delete against that advance. Resolution: suppress the fold while a session is active (the committer already has access to the coalescing session accessor), keeping the overlay authoritative. The transaction saving this unit targets is Immediate-mode, where each drain step currently issues its own commit + ack; coalesced mode already batches.

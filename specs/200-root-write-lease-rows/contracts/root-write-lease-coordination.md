# Contract: `IWorkflowExecutableStore` root-write lease and deletion-guard behavior

The public interface does not change: no member is added or removed, and no signature changes. This document pins the observable behavior that every implementation (in-memory and EF) must provide after spec 200. Contract tests assert it.

## Lease operations

| Operation | Returns | Behavior |
|---|---|---|
| `TryAcquireRootWriteLeaseAsync(artifactId, leaseId, expiresAt, now)` | lease or `null` | Returns `null` when the artifact is missing or a live deletion guard exists, including one committed concurrently with this call. A live lease with the same `leaseId` returns the existing token. Otherwise it grants a new lease. It **never fails or retries because another holder took, renewed or released a different lease on the same artifact.** |
| `RenewRootWriteLeaseAsync(lease, expiresAt, now)` | `bool` | `true` exactly when the lease is live, the token matches, and the incarnation is current. Other holders' activity never affects it. It reads only the holder's own lease record, never the executable pair, so an orphaned or corrupt pair yields `false` rather than an exception (owner decision, PR #2539). |
| `ReleaseRootWriteLeaseAsync(lease)` | — | Removes the lease when the token matches. Otherwise it is a no-op, including after the artifact has been deleted and when the pair is orphaned or corrupt (owner decision, PR #2539). Other holders' activity never makes it fail. |

## Deletion guard operations

| Operation | Returns | Behavior |
|---|---|---|
| `TryBeginDeletionAsync(artifactId, operationId, expiresAt, now)` | guard or `null` | Returns `null` while any unexpired lease exists. That covers new per-lease records, legacy leases in the shared record, and leases committed concurrently with this call. It also returns `null` while another operation holds a live guard. The same `operationId` returns the existing guard. |
| `CancelDeletionAsync(guard)` | `bool` | Unchanged. |
| `DeleteAsync(guard, now)` | `bool` | Re-verifies, atomically with the delete, that the guard is current and that no unexpired lease exists. Then it removes the artifact and all of its lease records. |
| `DeleteAsync(artifactId)` (unguarded) | — | Unchanged. Also removes all of the artifact's lease records. |

## Race outcomes (both orders)

| Interleaving | Required outcome |
|---|---|
| A lease is committed, then a guard begins | The guard is refused. |
| A guard is committed, then a lease is requested | The lease is refused. |
| A lease commit and a guard commit overlap | At most one succeeds. A refused acquirer leaves no live lease behind. A refused guard is cancelled. |
| A guarded delete commits while an acquirer is between its lease commit and its check | The acquirer returns `null` and withdraws its record. The artifact stays deleted. |
| The artifact is recreated after a delete | Leases from before the delete are not honoured (incarnation fencing). |

## `IWorkflowExecutableRootWriteLeaseManager.ExecuteAsync`

- The write succeeds and every release succeeds: the call returns success.
- The write succeeds but a release fails: the call returns success. One Warning diagnostic is logged per failed release, and the lease is left to expire.
- The write fails: the write's exception surfaces unchanged, whether the release succeeds or fails.
- A renewal reports the lease lost during the write: the write is cancelled and the lease-lost exception surfaces. This is unchanged.

## Checkpoint commit lease identity

- Each checkpoint commit attempt takes the lease id `checkpoint:{CommitId}:{nonce}`, with a fresh nonce per attempt.
- Replay of an already durable `CommitId` resolves before any lease is taken. This is unchanged.

## Catalog impact

- Runtime `EXTENSION_POINTS.md`, under "Lease identity": add that checkpoint lease ids are per attempt, and update the store behavior notes. The row layout is not recorded there because it is an implementation detail.
- No new extension point is added.

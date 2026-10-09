# Research: Root-Write Lease Coordination Without a Hot Row

All facts below are cited against `main` at `2b7d79c` unless stated otherwise. A label marks each decision as owner-made or a plan default.

## R1 — Source of the hot row (verified)

- **Fact.** `EfWorkflowExecutableStore` stores every live lease and the deletion guard of an artifact as JSON in one row of `elsa_runtime_workflow_executable_coordination` (`CoordinationState`).
- **Fact.** Acquire, renew, release, begin-deletion and cancel-deletion each perform a read-modify-write of that row. The write is guarded by an optimistic `Revision`/`IncarnationId` check and retried by `EfWriteRetry(16, Concurrency)` with no delay (`EfWorkflowExecutableStore.cs:17`).
- **Fact.** `EfRuntimeCheckpointCommitStore` runs every workflow-execution checkpoint inside `IWorkflowExecutableRootWriteLeaseManager.ExecuteAsync`, with lease id `checkpoint:{CommitId}`. The manager acquires the lease, renews it every duration/3 (about 20 s) and releases it in `finally`.
- **Consequence.** Every commit of every execution of one published workflow writes the same row at least twice.
- **Reproduced.** The three SQLite tests and the PostgreSQL parallel test on this branch fail with the production messages: SQLite 3/3, PostgreSQL 13/160 cycles, and CI on `a55706a` and `3d1706c` agrees.

## R2 — Mutual exclusion without a shared record (owner decision 3: write-then-check)

**Decision.** Use write-then-check. Each side commits its own record first, then reads the other side's record in a new statement that is not inside an earlier snapshot.

- **Acquire:** commit the lease row, then read the guard and incarnation. If a live guard is found, or the artifact is gone or recreated, delete our own lease row and return `null`.
- **Begin-deletion:** commit the guard on the coordination row, then count live lease rows plus live legacy leases (R5). If any exist, cancel the guard and return `null`.
- **Guarded delete:** re-check, inside its transaction, that the guard matches and no live lease exists, then delete the executable, coordination and lease rows together.

**Correctness argument.**
- Let A be an acquirer. It commits its lease at tA, then reads at rA > tA.
- Let G be a collector. It commits its guard at tG, then reads at rG > tG.
- If both succeed, A's read saw no guard, so rA < tG. G's read saw no lease, so rG < tA.
- Then tA < rA < tG < rG < tA, which is a contradiction. At most one of them succeeds.

The argument needs one property: a read issued after a commit has completed observes that commit. Each provider gives this as long as the check runs as a fresh statement outside any transaction opened before the other side's commit:

| Provider | Default isolation | Fresh-read property |
|---|---|---|
| PostgreSQL | READ COMMITTED | Each statement sees everything committed before it starts. |
| SQL Server | READ COMMITTED, with or without RCSI | With RCSI the statement-level snapshot is taken at statement start. Without it, shared locks block on uncommitted writes. Either way the read sees prior commits. |
| MySQL InnoDB | REPEATABLE READ | The consistent snapshot is taken at the first read *of the transaction*. An autocommit check statement after our own commit starts a new snapshot. |
| SQLite | Serialized writer; WAL readers | A new read transaction sees the latest committed writer. |

The implementation runs each step on a dedicated context with no ambient transaction (R4).

**Guarded delete versus a late acquirer.** Suppose a lease is committed after the delete's lease check but before the delete commits. The acquirer's check then sees either the live guard (the delete has not committed yet) or a missing or recreated artifact (the delete has committed). In both cases the acquirer withdraws its lease and returns `null`. The delete removes all lease rows of the artifact, so any row it misses is fenced by incarnation (R6).

**Alternatives considered.**
- *Shared or exclusive locks on the guard row*, using provider SQL such as `FOR SHARE` or `UPDLOCK, HOLDLOCK`. Rejected by owner decision 3: it needs provider-specific SQL and recreates lock traffic on one shared row.
- *Retry backoff only.* Rejected because it keeps the serialization point (spec Assumptions).

## R3 — Lease row operations

All operations run on the isolated context from R4.

- **Acquire.**
  - Reads the pair to get the incarnation and guard.
  - Inserts the row with primary key `Hash(scope, artifactId, leaseId)`, and finally runs the R2 check.
  - If the same id is already live for the same incarnation, it returns the existing token without writing. This keeps the #2274 shared-token behavior.
  - On a primary-key conflict it reloads and returns the winner's token, retrying under `UniqueKey`.
  - An expired row, or a row from another incarnation, is overwritten by a compare-and-swap on `Revision`.
- **Renew.** One `ExecuteUpdate`, filtered on id, token, `ExpiresAtUtcTicks > now` and incarnation. It returns `true` exactly when one row was affected. It never touches other leases.
- **Release.** One `ExecuteDelete`, filtered on id and token. Deleting nothing is a no-op, as today.

**Consequence.** No lease operation reads or writes another holder's row, so FR-001 and FR-002 hold by construction.

## R4 — Shared `RuntimeDbContext` hazard (verified registration; interaction to be pinned by a test)

**Facts.**
- `EfWorkflowExecutableStore` is registered scoped with the scoped `RuntimeDbContext` (`RuntimeArtifactsEntityFrameworkCoreRegistration.cs:160-231`).
- `EfRuntimeCheckpointCommitStore` takes the same scoped context.
- The commit store documents that it keeps pending sibling mutations a caller staged on that context (`EfRuntimeCheckpointCommitStore.cs:105-108`).
- Today's acquire calls `context.ChangeTracker.Clear()` before reading, and renewal runs in the background while the commit uses the context.

**Risk.** A per-lease insert through `SaveChanges` on the shared context would persist staged sibling changes outside the checkpoint transaction. The existing `Clear()` may already discard them. A renewal that fires during a commit longer than 20 s may use the context concurrently.

**Decision (plan default).** Lease and guard operations run on a dedicated short-lived `RuntimeDbContext`, resolved from a child scope through `IServiceScopeFactory`. That scope carries the same persistence access context. The store's other operations keep the injected context.

**Test.** Changes staged on the caller's context are neither saved nor discarded by acquire, renew or release. A renewal concurrent with an open commit transaction does not touch the caller's context.

**Alternatives considered.**
- *Provider-specific raw `INSERT` statements.* Rejected: they duplicate per-provider SQL.
- *Keep the shared context and reorder calls.* Rejected: it is fragile and leaves the background renewal on the shared context.

## R5 — Leases written before the upgrade (owner decision 1)

**Decision.** After the upgrade, the `Leases` dictionary in the coordination JSON is never written again. Guard grant and guarded delete treat any unexpired lease still in it as live. Expired legacy entries are dropped whenever the guard path rewrites the row. Within one lease duration (default 1 min) the legacy set is empty.

**Out of scope (ADR 0073 D5).** Mixed-version clusters, where old binaries keep writing old-format leases next to new ones, are unsupported. Pre-GA requires no mixed-version data compatibility. Legacy entries can only come from processes that existed before the restart.

## R6 — Incarnation fencing and cleanup

- Lease rows carry the `IncarnationId` that was current when they were granted.
- Every check compares the row's incarnation with the current coordination incarnation. A row from an earlier incarnation is treated as absent and overwritten on reuse.
- Guarded delete and the unguarded `DeleteAsync` remove the artifact's lease rows in the same transaction as the pair.
- Crash-orphaned expired rows on a live artifact are purged, in bounded batches, by the begin-deletion and guarded-delete paths. They are also overwritten when their id is reused.
- **Accepted residual:** an artifact that is never collected keeps crash-orphaned rows. There is one inert row per crashed holder, and they never block anything. No new store contract member is added for a global sweep. If accounting shows growth, that becomes a follow-up.

## R7 — A durable commit is isolated from release failure (FR-011)

**Facts.**
- `WorkflowExecutableRootWriteLeaseManager.ExecuteAsync` calls `ReleaseAllAsync` in `finally`, and that method rethrows the first release failure.
- So a release failure replaces a successful write, and also replaces an original write failure.

**Decision.**
- `ReleaseAllAsync` records failures and never throws. The manager logs each one at Warning, with artifact id and lease id, through an injected optional `ILogger<WorkflowExecutableRootWriteLeaseManager>`. Telemetry gets no new metric.
- Write success is reported as success, and a write failure surfaces unchanged.
- A renewal failure during the write keeps its current semantics: the lease was lost, the write is cancelled, and the result is a failure.
- An unreleased lease simply expires.

## R8 — Per-attempt checkpoint lease identity (#2286, FR-015)

**Decision.**
- The EF checkpoint commit store uses `checkpoint:{CommitId}:{nonce}`, where the nonce is a fresh GUID per attempt.
- The in-memory checkpoint commit store applies the same rule.
- Replay resolution still happens before any lease is taken, so idempotency is untouched.
- Activation leases (`activation:{id}:{nonce}`) and test-run leases are unchanged.

**Test.** Two overlapping commits of one `CommitId`: the first releases, and the second keeps its lease and renews successfully. This test fails on today's code.

## R9 — Schema and migrations

**Decision.**
- Add the table `elsa_runtime_workflow_executable_root_write_lease` (data-model.md).
- Generate an incremental migration per provider, following the existing Runtime chain (for example `*_RecurringTriggerOccurrenceClaims`), through `tools/ef/Elsa.EntityFrameworkCore.Tooling`.
- The change is expand-only: no column is dropped or retyped, and the coordination JSON shape is unchanged.
- Register the entity:
  - in the `RuntimeArtifact` schema family in `AssemblyInfo.cs`;
  - in the text-column list in `RuntimeProviderContexts.cs`;
  - with its own `SchemaVersion` stamp.
- The existing migrations test suite proves model and migration match, plus a fresh install, on all four providers.

## R10 — Verification environment

- The authoring container has no access to the preview package feeds. Packages rebuilt from source into a local folder feed allow local builds and tests (recipe in PR #2539). Results are dev-loop evidence. CI remains authoritative.
- PostgreSQL runs as a local 16.15 cluster. SQL Server and MySQL are covered by the CI container suites.
- The A1 end-to-end matrix (SC-001) needs the Azure runner (`elsa-p2382-98e7664aa3`).

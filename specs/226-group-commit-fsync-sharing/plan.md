# Plan: group-commit / cross-drain fsync sharing (spec 115)

## Seam (evidence-selected)

Elsa-side gateway at the `IRuntimeCheckpointCommitStore` durable-write boundary — a process-wide
coordinator that folds concurrent commits into one Groundwork `IDocumentUnitOfWork` (one transaction,
one fsync). Chosen over a Groundwork-side change because the store API already supports multi-commit
fan-in, it stays measurable in this repo, it is provider-agnostic, and it preserves the durability-ack
contract (whereas a deeper Groundwork fsync change would relax it). See spec.md §Seam decision and
research.md for the store-API and SQLite-writer evidence.

## Components

1. **`RuntimeGroupCommitCoordinator`** (`src/Elsa/Persistence/Groundwork/Stores/`) — singleton.
   Leader/follower group commit over one `SemaphoreSlim(1,1)` gate + a `ConcurrentQueue`. The gate holder
   flushes every same-tenant queued commit (≤ `MaxBatchSize`) into one unit-of-work + one `CommitAsync`,
   then releases; followers wake already-committed. No timer/window. A batch of one degrades to the
   single-commit fallback (no solo regression). Any member failure rolls the shared transaction back and
   re-drives every member individually. Exposes deterministic batch counters.

2. **`RuntimeGroupCommitOptions`** (`.../Stores/`) — `MaxBatchSize` (default 64, must be > 1).

3. **`GroundworkRuntimeCheckpointWriter` refactor** — extract `ApplyStagedAsync(IDocumentStore, commit,
   fingerprint, ct)` (stage all state changes + create-only marker into an already-open unit-of-work,
   no commit) from `ApplyAtomicallyAsync`. Add `WriteAsync` that routes the leased write either to
   `ApplyAtomicallyAsync` (coordinator absent) or `coordinator.SubmitAsync(stage: ApplyStagedAsync,
   fallback: ApplyAtomicallyAsync)`. `BatchTenantKey` = workflow-execution `TenantId` (unit-of-work scope
   resolver forbids mixed-tenant batches). Optional coordinator ctor param → null keeps today's path.

4. **`GroundworkGroupCommitRegistration.AddGroundworkRuntimeGroupCommit(...)`** — opt-in singleton
   coordinator + options. Default off (not called) until measured.

5. **`EngineConcurrencyBenchmarks.ConcurrencyScalingCurve_GroupCommit`** — shared-sqlite A/B (OFF vs ON)
   at N ∈ {1,8,32,128}, run-order swapped, reporting walls + per-run commit markers + coordinator
   counters. In the benchmark's N-container topology one shared coordinator instance is injected across
   all harnesses (mirroring production's single host-wide coordinator).

## Correctness argument (maps to FR-3..FR-5)

- **Ack after durable (FR-3):** `member.Result` is returned only after the shared `CommitAsync` returns;
  the committer's post-commit publish runs after that, unchanged.
- **Failure isolation (FR-4):** shared unit-of-work is all-or-nothing; on any member throw (stale fence,
  marker replay, uncertain-ack) the batch rolls back and each member re-drives through
  `ApplyAtomicallyAsync` — its own retry loop and marker-idempotent replay reconcile it exactly as today.
  Re-drive after an uncertain shared commit is safe: the create-only marker makes it a replay hit.
- **Byte-identical + 1 marker/run (FR-5):** members are staged through the same `ApplyStagedAsync` the
  single path uses; each still writes its own create-only marker, so per-run marker count stays 1.
- **No deadlock:** coordinator gate is always taken before the store's writer connection gate (only the
  leader holds both; the fallback releases the coordinator gate before opening its own unit-of-work).

## Test plan

- Coordinator unit tests: concurrent same-tenant commits fold (batch counters > 0, all durable, 1
  marker/run); a solo commit never batches; a poisoned member (stale fence) degrades the batch and every
  other member still commits exactly once; different tenants never share a transaction.
- Full `Elsa.Persistence.Groundwork.Tests` + `Elsa.Workflows.Runtime.Tests` (writer refactor is
  behavior-preserving with coordinator absent) + Groundwork crash-convergence suites.
- Benchmark A/B as the perf gate.

## Decision gate

Keep + wire a default only if the A/B shows a real cross-drain win with zero N=1 regression. Otherwise
land the coordinator opt-in/off (or report the killed hypothesis) per research.md.

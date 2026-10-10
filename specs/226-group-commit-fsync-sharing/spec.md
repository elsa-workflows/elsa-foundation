# Feature Specification: Group-commit / cross-drain fsync sharing on the shared durable writer

**Feature Branch**: `worktree-agent-a724c44cc4cee864a`

**Created**: 2026-07-21

**Status**: Draft (design + implementation; measurement gated — see [research.md](./research.md))

**Input**: Engine-performance work unit under the Runtime Execution Seam bucket. Spec 114
(`EngineConcurrencyBenchmarks`, PR #900) proved that per-run commit cost is at its floor (exactly 1
durable commit per hot-loop run) and that the remaining throughput ceiling under concurrency is the
**shared SQLite single writer**: N=128 shared-sqlite = 1.5 runs/s vs isolated-sqlite = 4.5 runs/s — a
3× loss from pure write serialization, identical work, identical 1 commit/run. The recommended lever is
classic database **group commit**: when K concurrent drains each need one durable commit, let their
writes share one transaction/fsync instead of K serialized ones.

## Context

This is an **optimization**, not an instrument. Per-run coalescing (specs 105–113) has driven each run
to 1 durable commit; that cannot go lower per run. The orthogonal lever is *cross-run*: batch the
single-commit-per-run of many **concurrent** drains into one shared durable transaction.

Two structural facts (both verified against source) shaped the seam choice:

1. **The Groundwork document unit-of-work API supports fan-in.** `IDocumentStore.BeginAsync(scope)`
   returns one `IDocumentUnitOfWork`; arbitrarily many `SaveAsync`/`DeleteAsync` from any callers can be
   staged into it and made durable with one `CommitAsync` = one SQLite transaction. Elsa already uses
   exactly this pattern for one checkpoint across ~15 document kinds
   (`GroundworkRuntimeCheckpointWriter.ApplyAtomicallyAsync`). A gateway can therefore stage **N runs'**
   checkpoints into one unit-of-work and commit once.

2. **The SQLite single writer is a `SemaphoreSlim(1,1)` + one connection.** Every unit-of-work holds the
   single writer connection for its whole lifetime; there is no group-commit, write-queue, or batching
   layer in Groundwork. Collapsing N unit-of-work commits into one collapses N writer-gate acquisitions,
   N transactions, and N WAL-append/commit cycles into one.

## Seam decision

**Elsa-side group-commit gateway** (option (a) in the unit brief), chosen with evidence:

- The store's atomic-write API **carries multiple commits' documents in one transaction** (fact 1), which
  is the stated precondition for the preferred Elsa-side seam.
- Staying in `Elsa.Persistence.Groundwork` keeps the optimization **provider-agnostic** (every
  `CrossUnitAtomic` document store — SQLite, Postgres, SqlServer, Mongo — benefits) and **measurable from
  this repo's `EngineConcurrencyBenchmarks`**.
- The Groundwork-side seam (option (b)) was rejected: (i) the local Groundwork clone is a *different
  revision* than the consumed `0.0.1-preview.77` package, so a clone edit would not reflect what Elsa
  runs and could not be E2E-measured pre-publish; (ii) the package already runs WAL + `synchronous=NORMAL`,
  so a deeper fsync-relaxation there would weaken — not preserve — the durability-ack contract this unit
  must keep non-negotiable.

## Requirements

- **FR-1 Leader/follower group commit.** Concurrent `IRuntimeCheckpointCommitStore.CommitAsync` calls
  funnel through a process-wide coordinator. Whoever holds the coordinator gate flushes every commit
  currently queued (up to a max batch size, same-tenant only) into one `IDocumentUnitOfWork` + one
  `CommitAsync`. No timer, no batch window.
- **FR-2 No solo-commit regression.** A lone committer (nothing else queued) is never batched and never
  waits for a window: a batch of one degrades to today's exact single-commit path
  (`ApplyAtomicallyAsync`, including its fence/marker retry loop). N=1 pays only an uncontended queue +
  semaphore round-trip.
- **FR-3 Durability-ack semantics (ADR 0020).** A member's result (and thus its post-commit intents) is
  released only after the shared `CommitAsync` returns — i.e. after the batched bytes are durably synced.
  Group commit shares the fsync; it never defers the ack past it.
- **FR-4 Atomicity + failure isolation.** Each run's commit stays individually atomic. Because the
  Groundwork unit-of-work is all-or-nothing (one member's optimistic-concurrency conflict poisons the
  whole transaction), any member failure rolls the shared unit-of-work back and **re-drives every member
  individually** through the single-commit path (today's behavior). No member is lost or half-applied.
- **FR-5 Byte-identical durable state + counts.** Each run persists exactly the documents and the single
  commit marker it persists today; per-run commit-marker count stays 1/run. The only change is how many
  physical transactions carry them.
- **FR-6 Config.** A toggle + `MaxBatchSize` following existing options patterns. Default **off** until
  the N=1..128 measurement (research.md) shows a real win with zero solo regression.

## Non-goals

- No change to immediate mode, coalescing, single-writer-per-execution fencing, or the scheduler.
- No Groundwork edits.
- No cross-tenant batching (the unit-of-work scope resolver forbids mixed-tenant transactions).

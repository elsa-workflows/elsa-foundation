# Feature Specification: Concurrency / throughput instrument for the in-process runtime

**Feature Branch**: `worktree-agent-a262ede19cf609747`

**Created**: 2026-07-20

**Status**: Draft (instrument delivered; scaling curve captured — see [research.md](./research.md))

**Input**: Engine-performance work unit under the Runtime Execution Seam bucket. The campaign so far
(ADR 0031/0032, specs 105–111) optimized **single-run** latency: durable SQLite hot-loop×10 went
3856 ms / 66 commits → 380 ms / 1 commit (Coalesced + ReplaySafe + burst cache). Nothing has ever
measured **N concurrent** workflow executions. This unit builds the instrument and reports the scaling
curve, so the next optimization unit is chosen from evidence, not guessed.

## Context

This is an **instrument, not an optimization**. It adds no product code and changes no runtime behavior.
It extends the existing engine benchmark harness (`benchmarks/.../EngineExecutionBenchmarks.cs`,
`DurableRoundTripDiagnostics.cs`) with a concurrency benchmark that drives N concurrent 10-activity
hot-loop bursts and reports the curve. There are **no hard performance assertions** (they would flake);
each run only asserts its workflow completed. Durable checkpoint-commit counts are the deterministic
evidence; wall times are reported with the usual measurement caveats.

Two structural facts about the runtime shaped the design (both verified against source):

1. **The in-process actor provider has no global drain/concurrency cap.**
   `InProcessWorkflowExecutionActorProvider` serializes commands only *per workflow-execution id* (a
   per-actor `SemaphoreSlim(1,1)` mailbox). Distinct execution ids get distinct actors and drain fully
   in parallel — bounded only by the thread pool and the durable store. There is no engine-level
   semaphore that caps how many workflows drain at once.
2. **The durable store is the only shared resource under contention** in a single-host deployment: all
   workflow instances share one database, and on-disk SQLite has a single writer (WAL serializes
   writers).

Together these predict — before measuring — that the bottleneck the instrument finds will be the
**store single-writer**, not a drain cap. The instrument's job is to confirm or refute that and to
quantify the curve.

## Scope boundary

- **In scope**: a concurrency benchmark (`benchmarks/.../EngineConcurrencyBenchmarks.cs`) that runs N
  concurrent hot-loop×10 executions for N ∈ {1, 8, 32, 128} across three backends that peel off one cost
  layer at a time, plus the shared graph builders extracted so the concurrency suite and the single-run
  suite build byte-identical graphs from one place (`BenchmarkWorkflows`), plus the minimal, additive
  harness parameterization (per-execution id + identity) needed to run distinct executions against one
  shared store.
- **Explicitly NOT in scope**:
  - Any product/runtime code change or optimization. The finding *selects* the next optimization unit;
    it does not implement one.
  - Hard latency/throughput assertions.
  - Distributed / multi-host placement (the in-process provider is the subject; the distributed provider
    is a separate seam).
  - A bespoke load-generation framework. The instrument reuses the existing harness and the existing
    Testcontainers Postgres driver; it builds no heavy new infrastructure.

## Backends (the three-way isolation)

Each backend removes one layer, so reading the deltas names the bottleneck:

| Backend | Store | What it isolates |
|---|---|---|
| **in-memory** | runtime default in-memory stores, per execution | pure CPU + scheduling scaling (no fsync) — the concurrency ceiling |
| **isolated-sqlite** | one on-disk SQLite DB **per execution** | durable fsync cost, but each run on its own writer (no cross-run contention) |
| **shared-sqlite** | **one** on-disk SQLite DB shared by all N | the real single-host shape: one contended durable writer |

- Delta **in-memory → isolated-sqlite** = the durability / fsync tax.
- Delta **isolated-sqlite → shared-sqlite** = SQLite single-writer contention.

The durable backends run the **shipping** configuration: Coalesced checkpoint persistence (segment cap
above the burst so it never trips), ReplaySafe leaves, burst-scoped executable cache on.

## Why N independent harnesses sharing one store

The instrument stands up N one-actor harnesses (each its own DI provider) over one shared document store,
rather than one host with N actors. Because the actor provider has no global drain cap (fact 1 above), the
two are behaviorally equivalent for drain concurrency, and N independent harnesses are far simpler to build
on the existing harness. Each harness gets a **distinct execution id + executable identity** so the shared
store partitions the runs cleanly (activity-state, scheduler-queue, and executable documents all key on
those ids). Per-provider setup (DI build, activity-type registry scan) is paid **before** the timed window,
so wall time measures dispatch + drain + store I/O, not engine construction.

## Postgres comparison

The Testcontainers-based Groundwork Postgres driver (`PostgreSqlGroundworkProviderDriver`, in the
already-referenced `Elsa.Persistence.Groundwork.Testing` project) is reused to add a shared-Postgres backend
where feasible — benchmarks never run in CI, so a Docker dependency is acceptable here. Postgres has real
concurrent writers (MVCC), so the shared-Postgres curve is the direct counterfactual to shared-SQLite's
single writer: if single-writer serialization is the SQLite bottleneck, Postgres should scale better at high
N. If reuse proves awkward, the backend is skipped and the reason recorded (see research.md) rather than
building new infrastructure.

## Success Criteria

- **SC-001**: The benchmark project builds and the concurrency benchmark runs to completion for
  N ∈ {1, 8, 32, 128} on at least the in-memory, isolated-sqlite, and shared-sqlite backends.
- **SC-002**: For each (backend, N) the instrument reports total wall time, per-run p50/p95 (and min/max)
  latency, aggregate durable checkpoint commits, and derived commits/run and throughput.
- **SC-003**: Durable checkpoint-commit counts are reported and are deterministic (stable across the
  aggregate), matching the shipping single-run cadence scaled by N.
- **SC-004**: The scaling curve and a bottleneck analysis (what saturates first, with evidence) are
  recorded in research.md, along with a recommendation for the next optimization unit.
- **SC-005**: No product/runtime code is changed; harness changes are additive and existing test suites
  that use the harness continue to build and pass.

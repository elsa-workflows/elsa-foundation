# Group-commit headroom on Groundwork v2 (2026-08)

Evidence for [#1425](https://github.com/elsa-workflows/elsa-foundation/issues/1425), and the basis for
answering [#1233](https://github.com/elsa-workflows/elsa-foundation/issues/1233).

Instrument:
`benchmarks/Elsa/Workflows/Runtime/Benchmarks/EngineConcurrencyBenchmarks.ConcurrencyScalingCurve_SharedWriterHeadroom`.

## Why this arm exists

`RuntimeGroupCommitCoordinator` batched flushes on the v1 document store and went with that substrate in
[#1420](https://github.com/elsa-workflows/elsa-foundation/pull/1420). `ConcurrencyScalingCurve_GroupCommit`
A/B'd it ON against OFF, so it was deleted rather than pointed at something that no longer exists.

That leaves #1233 asking a question with no instrument: **does v2 need a group-commit equivalent?** An arm
that A/B'd a coordinator could not be written, because there is no coordinator. This one measures the same
quantity without presupposing one.

## Method

At each N, the two durable backends run the same graph back-to-back:

- **isolated-sqlite** — every execution owns its own database. Full durability cost, no cross-run write
  contention.
- **shared-sqlite** — all N executions share one database. The real deployment shape, where SQLite's
  single writer serializes them.

Both commit the identical number of checkpoints, so the **isolated → shared delta is the contention tax**,
and that tax is the entire prize a group-commit coordinator could compete for. If the curves track each
other, batching commits across drains has nothing to win on this substrate. If shared collapses away from
isolated as N rises, the delta is the upper bound on what a coordinator could recover.

Leaf shape is the **External CLR leaf** — unfusable, ~11 commits per run against the ReplaySafe leaf's 1,
so it puts the most durable writes through the contended writer per unit of engine work. N ∈ {1, 4, 8,
16, 32}, one shape, two backends: deliberately narrower than `ConcurrencyScalingCurve`, whose three shapes
over three backends to N=128 exhaust memory on a 24 GB host before finishing.

## Machine state

Apple Silicon, 8 logical processors, 24 GB. macOS. Five repeats, each gated to start below **load 4.0**
with a settle-wait between them; per-repeat load recorded at both ends. Load at start ranged 3.88–4.31.

This gating is not ceremony. An earlier unpaced set drove load from 3.4 to 25 through its own repeats and
swung the N=16 shared cell from 3725 ms to 8207 ms and back to 3780 ms — a 2.2× spread attributable
entirely to ambient load. That is the same quiet-machine distortion that produced spec 115's original
result and that #1233 warns about. Those runs were discarded.

## Results

Medians across the five repeats:

| N | isolated wall | shared wall | shared/isolated | isolated commits/s | shared commits/s | commits/s ratio |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 396 ms | 392 ms | **0.99** | 27.7 | 28.0 | 1.01 |
| 4 | 744 ms | 1105 ms | 1.48 | 59.1 | 39.8 | 0.67 |
| 8 | 1475 ms | 1730 ms | 1.17 | 59.7 | 50.9 | 0.85 |
| 16 | 2789 ms | 3707 ms | 1.33 | 63.1 | 47.5 | 0.75 |
| 32 | 5723 ms | 8522 ms | 1.49 | 61.5 | 41.3 | 0.67 |

Per-N spread of the wall ratio across repeats: N=1 0.94–1.01 · N=4 1.40–1.73 · N=8 1.12–1.21 ·
N=16 1.28–1.39 · N=32 1.16–1.57.

Commit counts are fixed by the graph (11 per run), so commits/s is the fsync throughput the writer
actually sustained. No level FAULTED in any of the 25 measured cells.

## What it says

**The N=1 control is 0.99.** With no contention the two backends are indistinguishable, so the instrument
is measuring contention rather than a difference between the backends themselves.

**The contention tax does not diverge.** It sits in a 1.17–1.49 band from N=4 to N=32 and never runs away.
This is the load-bearing observation: v1's group commit existed to rescue a curve that *collapsed* under
concurrency. This curve does not collapse — it settles at a roughly constant multiple. A coordinator that
amortises fsync across drains competes for that constant, not for a growing one.

**The engine, not the writer, is the limiter past N≈4.** Isolated commits/s saturates at ~60 from N=4
onward on 8 cores, so beyond that the durable path is not what is holding throughput back. Any writer-side
optimisation is bidding for a share of an already-bounded quantity.

**v2 already coalesces where v1's coordinator did.** `GroundworkV2RuntimeCheckpointWriter` commits a whole
checkpoint through one `BeginUnitOfWork` with `BatchWriteOptions.Exact`, and `Groundwork.Store` carries
`BatchContext` and `CoalescedWrite` beneath it. The per-checkpoint batching v1 added on top of a document
store is inside the v2 substrate. What a coordinator would add is *cross-drain* fsync sharing on top of
that — a strictly smaller increment than v1's was.

## What this does not measure

Three configurations #1233 names are **not** covered here, and the gap is not incidental:

- **`synchronous=FULL`** is unreachable. `SqliteProviderFactory.Create` takes a connection string, and
  Microsoft.Data.Sqlite exposes no `synchronous` or `journal_mode` keyword; the pragma is per-connection
  and the provider owns the connection. Measuring the expensive-fsync case needs provider support first.
- **PostgreSQL** is *blocked*, not merely unmeasured. The runtime cannot commit a checkpoint on PostgreSQL
  at all: every transactional write to a JSON column fails with
  `42804: column "content" is of type jsonb but expression is of type text`
  ([#1432](https://github.com/elsa-workflows/elsa-foundation/issues/1432)). The shared-Postgres arm was run
  and this is how that defect was found.
- **Network filesystems** were not attempted.

So the honest scope is: **on SQLite in its default journal mode, on one 8-core host.** That is exactly the
configuration spec 115 already measured and explicitly said was insufficient to decide on. This report
narrows the question rather than closing it.

## Recommendation

**Do not build a group-commit coordinator for v2 on this evidence.** The measured headroom is a constant
~1.2–1.5× on wall time and ~15–33% of commit throughput, it does not grow with concurrency, no level
faults, and the substrate already batches per checkpoint. Speculative work against a bounded, flat prize
is the wrong trade against the interaction cost group commit carries with the single-writer discipline,
lease fencing and failure semantics.

**Do not close #1233 as decided, either.** The configuration where group commit was designed to pay —
expensive fsync — is the one that could not be measured. The correct disposition is: no coordinator on
current evidence; revisit if and when `synchronous=FULL` becomes reachable and PostgreSQL can commit at
all.

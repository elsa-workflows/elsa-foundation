# Research: concurrency scaling curve + bottleneck analysis (spec 114)

> **Leaf-shape correction (2026-08).** Every curve below was measured with `NoOpStep`, a `ReplaySafe` leaf that
> exists only in the benchmark assembly and commits once per run. No *shipped* leaf activity is `ReplaySafe`, so
> these numbers describe the fusable floor, not production traffic. The curve has since been re-run over three leaf
> shapes — see
> [Concurrency curve re-measured with production leaf shapes](../../docs/reports/concurrency-curve-production-shapes-2026-08.md).
> The bottleneck analysis in §Bottleneck analysis stands; the **shape** does not. The `External` shape pays 11
> commits and 56 dispatches per run, its throughput curve has no rising region at all (it peaks at N=1, where this
> one peaks at N=32), and at N=128 it converts into lease-expiry faults rather than merely slow runs. Size admission
> control off that report, not off the table below.

Instrument: `benchmarks/Elsa/Workflows/Runtime/Benchmarks/EngineConcurrencyBenchmarks.cs`
(`ConcurrencyScalingCurve` + `ConcurrencyScalingCurve_SharedPostgres`). Method: N concurrent hot-loop×10
executions, N ∈ {1, 8, 32, 128}, backends that peel off one cost layer at a time. Durable backends run the
shipping configuration (Coalesced + ReplaySafe + burst cache, segment cap 256). Per-provider setup is paid
before the timed window; wall time is dispatch + drain + store I/O over `Task.WhenAll` of the N runs.

## Measurement environment

- Machine: 8 logical processors (Apple Silicon, Darwin 25.5.0).
- **Load caveat (important)**: the machine was busy throughout capture — the primary SQLite run started at
  1-min load ~13 and climbed into the 30s; the Postgres run coincided with a load spike to ~90. Absolute
  wall times and p50/p95 are therefore inflated and noisy — read them as **shape and cross-backend ratios**,
  not clean latency numbers. **Checkpoint-commit counts are deterministic and load-proof** and are the
  primary evidence. Backends are measured back-to-back, so their relative ordering within a run is more
  trustworthy than any absolute value. A clean re-run on a quiet machine is advised to firm up magnitudes;
  the *direction* of every finding below is robust to the load.

## Scaling curve — SQLite + in-memory (primary run, load ~13 → 30)

Total wall = wall clock for all N to finish; p50/p95 = per-run latency; commits/run = durable checkpoint
commits per run; throughput = N / total-wall.

| Backend | N | total wall (ms) | p50 (ms) | p95 (ms) | agg commits | commits/run | throughput (runs/s) |
|---|--:|--:|--:|--:|--:|--:|--:|
| in-memory | 1 | 225 | 225 | 225 | – | – | 4.4 |
| in-memory | 8 | 637 | 593 | 637 | – | – | 12.6 |
| in-memory | 32 | 1 667 | 880 | 1 162 | – | – | 19.2 |
| in-memory | 128 | 15 794 | 8 422 | 13 825 | – | – | 8.1 |
| isolated-sqlite | 1 | 562 | 562 | 562 | 1 | 1.0 | 1.8 |
| isolated-sqlite | 8 | 1 097 | 948 | 1 079 | 8 | 1.0 | 7.3 |
| isolated-sqlite | 32 | 5 885 | 3 615 | 5 846 | 32 | 1.0 | 5.4 |
| isolated-sqlite | 128 | 28 604 | 17 008 | 26 196 | 128 | 1.0 | 4.5 |
| **shared-sqlite** | 1 | 459 | 459 | 459 | 1 | 1.0 | 2.2 |
| **shared-sqlite** | 8 | 2 308 | 2 102 | 2 281 | 8 | 1.0 | 3.5 |
| **shared-sqlite** | 32 | 5 746 | 5 475 | 5 723 | 32 | 1.0 | 5.6 |
| **shared-sqlite** | 128 | **84 952** | 80 905 | 83 026 | 128 | 1.0 | **1.5** |

A second independent run (started at higher load) reproduced identical commit counts (1/run everywhere,
aggregate = N) and the same in-memory < isolated < shared ordering at N=128, confirming the commit evidence
and the direction. N=1 rows are single-sample and jittery under load; ignore their absolute values.

### Postgres (attempted; driver reuse works, high-N infeasible on the stock container)

Reusing the Testcontainers `PostgreSqlGroundworkProviderDriver` from the benchmark project **works** for
store creation and correctness: commits were a deterministic 1/run on Postgres too (N=1 → 1, N=8 → 8,
N=32 → 32). But:
- **N=128 is infeasible on the reused container**: PostgreSQL's default `max_connections` (~100) is exceeded
  because the N independent providers each pool their own connections (`53300: sorry, too many clients
  already`). That is a harness-topology ceiling (N independent engines), not an engine limit.
- The Postgres capture landed in a **load-~90 window**, so its wall numbers (e.g. N=32 → 65 s) are
  contaminated and are **not used as evidence**.

The Postgres backend is kept in the instrument (it degrades gracefully rather than failing) and confirms the
reuse path. A clean shared-Postgres MVCC comparison — the natural counterfactual to SQLite's single writer —
needs either a higher `max_connections` or a single shared connection pool across the N runs, plus a quiet
machine. That is left as a follow-up rather than building it here (per the unit's "don't build heavy new
infrastructure" boundary).

## Bottleneck analysis

**1. There is no engine-level drain / concurrency cap — refuted as a bottleneck.**
`InProcessWorkflowExecutionActorProvider` serializes commands only *per workflow-execution id* (a per-actor
`SemaphoreSlim(1,1)` mailbox); distinct ids get distinct actors and drain fully in parallel. The instrument
confirms it empirically: N workflows all run concurrently, and in-memory throughput climbs to ~19 runs/s at
N=32 with no fixed plateau. "Raise the drain cap" is therefore a non-unit — there is nothing capping it.

**2. The single durable writer is the throughput ceiling under concurrency — this is what saturates first.**
The load-proof isolated-vs-shared SQLite delta isolates it, because everything else is held equal (same
graph, same shipping cadence, same 1 commit/run):
- At **N=128**: shared-sqlite finishes in **84.9 s (1.5 runs/s)** vs isolated-sqlite **28.6 s (4.5 runs/s)** —
  a **3× throughput collapse purely from funnelling all runs through one SQLite writer**. Commits stay exactly
  1/run, so the cost is **write serialization + lock/backoff contention on the single writer**, not extra work.
- The gap **widens with N**: ~2× at N=8, ~3× at N=128 (N=32 sits inside the load noise, shared≈isolated). This
  is the signature of a serialized resource whose queue depth grows with offered concurrency.
- in-memory (no fsync) is the ceiling: **15.8 s / 8.1 runs/s** at N=128 — ~1.8× faster than isolated. That
  in-memory→isolated gap is the plain durability/fsync tax, present even without sharing.

**3. Per-writer throughput also sags at scale, independent of sharing.** isolated-sqlite throughput falls
7.3 → 5.4 → 4.5 runs/s from N=8 → 32 → 128 even though each run owns its DB. That points at per-connection
write-lock / WAL-checkpoint overhead and thread-pool pressure, a secondary (smaller) effect beneath the
single-writer one.

**Verdict**: the first thing to saturate is the **shared SQLite single writer**, not a drain cap and not raw
CPU. Coalescing has already driven each run to its floor of 1 durable commit; the remaining lever is the
number of *fsync trips*, which under concurrency is bounded below by "one per run per writer".

## Recommendation for the next optimization unit

**Group-commit (write-coalescing) across concurrent workflows on the shared durable writer.** Per-run
coalescing is maxed out (1 commit/run — can't go lower per run). The next win is orthogonal: batch the
single-commit-per-run fsyncs of many *concurrent* drains into one shared fsync / one writer transaction — the
classic database group-commit / commit-pipelining pattern. The instrument shows the headroom directly: at
N=128 the shared writer serializes 128 one-commit runs into 84.9 s while 128 independent writers do the same
work in 28.6 s. A durable-commit aggregator that lets K concurrent commits ride one fsync would move the
shared curve toward the isolated curve.

Sequencing / supporting notes for that unit:
- Pair it with SQLite writer tuning (WAL `busy_timeout`, autocheckpoint cadence) to attack the secondary
  per-writer sag seen in the isolated curve.
- For genuinely high-concurrency deployments, PostgreSQL's real concurrent writers (MVCC) are the natural
  substrate; a clean shared-Postgres comparison (connection ceiling lifted, quiet machine) should be captured
  before committing heavily to SQLite-specific group-commit, to size the two paths against each other.
- Do **not** spend a unit on a drain-concurrency cap — there isn't one, and the in-memory curve shows drain
  concurrency is not the ceiling.

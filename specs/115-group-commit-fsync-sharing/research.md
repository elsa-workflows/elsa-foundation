# Research: group-commit / cross-drain fsync sharing (spec 115)

## Store-API evidence (why the Elsa-side seam is viable)

- **Fan-in is first-class.** `IDocumentStore.BeginAsync(DocumentCommitScope)` → one `IDocumentUnitOfWork`;
  arbitrarily many `SaveAsync`/`DeleteAsync` stage into it and one `CommitAsync` = one DB transaction.
  Elsa already fans ~15 document kinds of one checkpoint into one unit-of-work
  (`GroundworkRuntimeCheckpointWriter.ApplyAtomicallyAsync`). A gateway can fan N runs' checkpoints into
  one unit-of-work the same way. `GroundworkDocumentUnitOfWorkStore` is the adapter that routes writes
  into an already-open unit-of-work.
- **The SQLite single writer is a `SemaphoreSlim(1,1)` + one connection** with no group-commit layer of
  its own; consumed package `Groundwork.Sqlite 0.0.1-preview.77` runs `journal_mode=WAL`,
  `synchronous=NORMAL`, `busy_timeout=5000`. Collapsing N unit-of-work commits into one collapses N
  writer-gate acquisitions, N transactions and N WAL-append/commit cycles into one.
- **The local Groundwork clone is a different revision** than preview.77 (missing the WAL/synchronous
  pragma code), so a Groundwork-side change could neither reflect production nor be E2E-measured from this
  repo — a second reason the Elsa-side seam is the right one.
- **Constraints honored:** the unit-of-work scope resolver forbids a mixed-tenant transaction (batching
  keys on tenant); the unit-of-work is all-or-nothing (one member's conflict poisons the batch → the
  gateway rolls back and re-drives each member individually).

## Measurement — the first design was wrong, and the instrument caught it

**Instrument:** `EngineConcurrencyBenchmarks.ConcurrencyScalingCurve_GroupCommit` — shared-SQLite A/B
(group commit OFF vs ON), N concurrent 10-activity hot loops, one shared coordinator across all harnesses
(models the single host-wide coordinator). The coordinator's batch counters are deterministic, load-proof
evidence of whether folding actually happened.

### Round 1 — release-gate-before-commit design: ZERO batching (deterministic)

The first coordinator released the flush gate *before* doing the durable commit (the commit ran in the
caller's fallback, outside the gate). Result at **every** level N ∈ {1,8,32,128}:
`BatchFlushCount = 0`, `BatchedMemberCount = 0`, `SoloFlushCount = N`. **The coordinator never folded a
single pair.** Cause: with the commit outside the gate, a leader holds the gate for only microseconds, so
concurrent runs almost never coincide at the gate — each run flushed solo. This is the classic group-commit
pitfall: the flush must happen *under* the serialization point, not after it.

### Round 2 — flush-pipeline design (commit under the gate): batching engages strongly

Holding the gate across the durable flush (like a database log mutex) makes arriving commits accumulate
while a leader commits, so the next leader folds them. Deterministic counters (load-proof), one paired run:

| N | batchFlushes | batchedMembers | soloFlushes | agg markers | commits saved (members−flushes) |
|---:|---:|---:|---:|---:|---:|
| 1 | 0 | 0 | 1 | 1 | 0 (solo, no regression) |
| 8 | 3 | 6 | 2 | 8 | 3 |
| 32 | 5 | 22 | 10 | 32 | 17 |
| 128 | 12 | 123 | 5 | 128 | **111** |

At N=128, **123 of 128 concurrent commits folded into 12 transactions** — a ~10× reduction in durable
transactions for the same work, with per-run marker count still exactly 1/run (correctness preserved). The
fraction folded rises with N, the signature of a working group commit: the more contended the single
writer, the more sharing.

### Wall time — heavily load-contaminated; paired ratios below

The machine was under severe, swinging ambient load throughout (1-min load 28–115 on 8 cores), so absolute
walls and single-order A/B pairs are unreliable — exactly the caveat spec 114 flagged. A single-order pass
showed N=32 ON 1.5× faster but N=8/128 confounded by ON drawing the higher-load slot. The final instrument
therefore measures **paired OFF/ON back-to-back with order alternated across repeats**; the load-robust
signal is the per-pair ON/OFF ratio, read together with the deterministic fold counts.

### Round 3 — paired pass under load 400+: discarded

An order-alternated paired pass ran while other sessions drove the machine to **1-min load 400+**. Walls
were 25–50× inflated and the run failed with a `RuntimeExecutionOwnershipLostException` — the
ownership-lease *heartbeat* background task was CPU-starved so the lease expired mid-drain. Environmental
artifact of the load, not a group-commit defect (the heartbeat renewal is independent of the commit path,
and the pre-existing `ConcurrencyScalingCurve` benchmark shares the same vulnerability). Discarded.

### Round 4 — paired OFF/ON, order-alternated ×3 (authoritative walls)

Re-run after the ambient load subsided: started at 1-min load ~164 and **fell to ~15 by the end**, so the
levels run later (N=32, then N=128 last) are progressively cleaner; N=128 rep 2 is the cleanest pair
captured. Each row is one back-to-back OFF/ON pair; `ON/OFF` < 1 means group commit is faster.

| N | rep | order | OFF wall (ms) | ON wall (ms) | ON/OFF | batchFlushes | batchedMembers | soloFlushes | degraded | markers/run |
|---:|---:|---|--:|--:|--:|--:|--:|--:|--:|--:|
| 1 | – | probe | 3 188 | 3 337 | 1.05 | 0 | 0 | 1 | 0 | 1.0 |
| 8 | 0 | OFF,ON | 10 548 | 6 580 | 0.62 | 0 | 0 | 8 | 0 | 1.0 |
| 8 | 1 | ON,OFF | 4 617 | 8 858 | 1.92 | 0 | 0 | 8 | 0 | 1.0 |
| 8 | 2 | OFF,ON | 4 123 | 5 602 | 1.36 | 0 | 0 | 8 | 0 | 1.0 |
| 32 | 0 | OFF,ON | 24 325 | 24 209 | 1.00 | 6 | 14 | 18 | 0 | 1.0 |
| 32 | 1 | ON,OFF | 10 791 | 13 614 | 1.26 | 5 | 13 | 19 | 0 | 1.0 |
| 32 | 2 | OFF,ON | 5 637 | 12 107 | 2.15 | 5 | 13 | 19 | 0 | 1.0 |
| 128 | 0 | OFF,ON | 133 127 | 89 082 | **0.67** | 13 | 120 | 8 | 0 | 1.0 |
| 128 | 1 | ON,OFF | 82 967 | 80 399 | 0.97 | 9 | 120 | 8 | 0 | 1.0 |
| 128 | 2 | OFF,ON | 63 525 | 44 436 | **0.70** | 18 | 119 | 9 | 0 | 1.0 |

Reading (walls carry the load caveat; counters are deterministic):

- **N=128 — the regime the unit targets — shows a consistent win**: 119–120 of 128 commits fold into 9–18
  shared transactions every rep, and walls improve 3–33% (ratios 0.67 / 0.97 / 0.70; geometric mean ≈ 0.77,
  ≈ 1.3× throughput). The cleanest pair (rep 2, lowest ambient load) is 63.5 s → 44.4 s.
- **N=32 is inconclusive-to-negative in this pass** (1.00 / 1.26 / 2.15) with partial folding (13–14/32).
  The 2.15 outlier rode an OFF draw of 5.6 s — far below every other N=32 OFF sample — under still-elevated
  load; but the direction at N=32 cannot honestly be claimed as a win from this data.
- **N=8 never batched in this pass** (soloFlushes = 8 in all reps): at low concurrency under load, commits
  rarely coincide at the gate, so ON ≈ OFF plus noise. (Round 2, at lower load, did fold 6/8 once — batching
  at N=8 is possible but marginal.)
- **N=1**: solo path never batches (soloFlushes=1, batchFlushes=0); the +5% delta is single-sample noise
  under load ~160 (round 2's cleaner probe measured +1.6%).
- **Correctness invariants held everywhere**: markers exactly 1/run at every level and rep (aggCommits = N),
  zero degraded batches, all workflows completed.

## QA evidence

- `dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj` —
  **642/642 passed** (includes the restart/crash-convergence and redelivery contracts and the new
  GroupCommit tests), re-run green after the degrade/cancellation-isolation fix.
- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj -c Release` —
  **1362/1362 passed** (writer refactor is behavior-preserving with no coordinator registered).
- `dotnet build Elsa.Server.slnx` — **succeeded** (constructor change is trailing-optional; no call-site
  breaks anywhere in the solution).

## Verdict

**The group-commit mechanism works and is correct.** Deterministic, load-proof evidence across every
measured pass: under saturation it folds ~94% of concurrent commits into shared transactions (119–123 of
128 into 9–18 transactions, a ~7–14× reduction in durable transactions) while keeping exactly 1 durable
marker per run, byte-identical state, failure isolation (poison → per-member re-drive under each member's
own token), and a solo path that never batches or waits. The correctness unit tests
(`GroundworkRuntimeCheckpointWriterTests.GroupCommit`) pass, and both full suites are green.

**The throughput win is real in the saturated regime but not uniform.** At N=128 — precisely the regime
spec 114 identified (3× collapse on the shared single writer) — paired walls improve consistently
(ON/OFF ≈ 0.67–0.97, ≈ 1.3× throughput on the geometric mean, ~1.4× on the cleanest pair). At N=32 the
pass is inconclusive-to-negative; at N=8 batching rarely forms. Because the win is not uniform across the
curve and the engine-perf bar for a *default-on* change is high, group commit lands **opt-in, default off**
(`AddGroundworkRuntimeGroupCommit`), with the reproducible A/B instrument
(`ConcurrencyScalingCurve_GroupCommit`) retained so a quiet-machine run — ideally also probing mid-N more
finely — can decide the default. This matches the campaign's discipline of gating perf defaults on
trustworthy measurement.

### Note for the next unit

Spec 114's 3× shared-vs-isolated gap is the *aggregate* single-writer serialization, of which the
checkpoint commit is only one of several per-run store round-trips (marker pre-read + root-write-lease
acquire/release also serialize on the same SQLite connection gate). Group commit folds the commits; a
complementary lever is to **reduce the number of serialized store round-trips per run** (e.g. fold the
lease touch and marker read into the checkpoint transaction), which attacks the same bottleneck from the
other side.
